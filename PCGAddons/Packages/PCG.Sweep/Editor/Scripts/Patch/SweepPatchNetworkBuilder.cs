using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	internal static class SweepPatchNetworkBuilder
	{
		private const float HeightTolerance = 0.1f;
		private const float CuspArcFactor = 2f;
		private const float MinPieceLength = 0.05f;

		private struct PieceRange
		{
			public int SplineIndex;
			public float Start;
			public float End;
			public bool Closed;
			public bool FreeStart;
			public bool FreeEnd;
		}

		internal static bool CanBuild(SweepSnapshot snapshot, out string failure)
		{
			failure = null;

			if (snapshot.ProfileClosed)
			{
				failure = "PatchRibbonProfileRequired";
				return false;
			}

			if (snapshot.ProfilePoints.Length != 2 || snapshot.ProfileSegments.Length != 2)
			{
				failure = "PatchRibbonProfileRequired";
				return false;
			}

			float2 a = snapshot.ProfilePoints[0];
			float2 b = snapshot.ProfilePoints[1];
			float tolerance = math.max(1e-6f, math.abs(b.x - a.x) * 1e-5f);
			if (math.abs(a.y) > tolerance || math.abs(b.y) > tolerance || math.abs(a.x - b.x) <= tolerance)
			{
				failure = "PatchRibbonProfileRequired";
				return false;
			}

			return true;
		}

		internal static SweepPatchNetwork Build(SweepSnapshot full, List<Spline> splines, float step, float maxStep, float maxAngleRad, int maxVertices, bool buildPatches, CancellationToken ct, Action reportProgress)
		{
			int splineCount = full.Frames.Length;
			int vpr = full.ProfilePoints.Length;

			float planWidth = math.abs(full.ProfilePoints[1].x - full.ProfilePoints[0].x) * MaxLut(full.WidthLut);
			planWidth = math.max(planWidth, 1e-3f);

			var curves = new List<SweepBoundaryCurve>(splineCount * 2);
			bool outOfBounds = false;

			for (int i = 0; i < splineCount; i++)
			{
				ct.ThrowIfCancellationRequested();
				reportProgress();

				var frames = full.Frames[i];
				var positions = SweepMeshBuilder.BuildRingPositions(full, i, ct, reportProgress, out bool pieceOutOfBounds);
				outOfBounds |= pieceOutOfBounds;

				for (int side = 0; side < 2; side++)
				{
					var points = new float3[frames.Length];
					var plan = new float2[frames.Length];
					var station = new float[frames.Length];

					for (int r = 0; r < frames.Length; r++)
					{
						float3 p = positions[r * vpr + side];
						points[r] = p;
						plan[r] = new float2(p.x, p.z);
						station[r] = frames[r].Distance;
					}

					curves.Add(new SweepBoundaryCurve
					{
						SplineIndex = i,
						Side = side,
						Points = points,
						Plan = plan,
						Station = station,
						Closed = full.SplineClosed[i]
					});
				}
			}

			float stationGuard = planWidth * CuspArcFactor;
			var coverage = new SweepRibbonCoverage(curves, math.max(step, planWidth));

			var hits = SweepBoundaryIntersector.Intersect(curves, HeightTolerance, stationGuard, ct, reportProgress);
			var clusters = SweepPatchClusterSolver.Solve(hits, curves, splineCount, planWidth, ct);

			for (int c = 0; c < clusters.Count; c++)
			{
				var cluster = clusters[c];
				for (int arm = 0; arm < cluster.ArmCount; arm++)
				{
					int spline = cluster.ArmSpline[arm];
					var left = curves[spline * 2];
					var right = curves[spline * 2 + 1];
					if (left.Plan.Length < 2)
						continue;

					float last = left.Station[left.Station.Length - 1];
					if (IsFirstArm(cluster, arm))
					{
						float2 startPoint = (left.Plan[0] + right.Plan[0]) * 0.5f;
						if (coverage.IsCovered(startPoint, spline, 0f, stationGuard))
						{
							cluster.AbsorbedStart[arm] = true;
							cluster.CutStart[arm] = 0f;
						}
					}

					if (IsLastArm(cluster, arm))
					{
						float2 endPoint = (left.Plan[left.Plan.Length - 1] + right.Plan[right.Plan.Length - 1]) * 0.5f;
						if (coverage.IsCovered(endPoint, spline, last, stationGuard))
						{
							cluster.AbsorbedEnd[arm] = true;
							cluster.CutEnd[arm] = last;
						}
					}
				}
			}

			var patchFailures = new string[clusters.Count];
			var valid = new bool[clusters.Count];

			for (int c = 0; c < clusters.Count; c++)
			{
				ct.ThrowIfCancellationRequested();
				reportProgress();

				if (!buildPatches)
				{
					valid[c] = true;
					continue;
				}

				var probeChord = new float3[clusters[c].ArmCount][];
				valid[c] = SweepPatchBoundaryBuilder.TryBuild(clusters[c], hits, curves, coverage, stationGuard, probeChord, probeChord, ct, out _, out string probeFailure);
				if (!valid[c])
					patchFailures[c] = probeFailure;
			}

			var cutsBySpline = new List<(float Start, float End, int Cluster, int Arm)>[splineCount];
			for (int i = 0; i < splineCount; i++)
				cutsBySpline[i] = new List<(float, float, int, int)>();

			var lengths = new float[splineCount];
			for (int i = 0; i < splineCount; i++)
				lengths[i] = splines[i].GetLength();

			for (int c = 0; c < clusters.Count; c++)
			{
				if (!valid[c])
					continue;

				var cluster = clusters[c];
				for (int arm = 0; arm < cluster.ArmCount; arm++)
				{
					int spline = cluster.ArmSpline[arm];
					float start = math.clamp(cluster.CutStart[arm], 0f, lengths[spline]);
					float end = math.clamp(cluster.CutEnd[arm], 0f, lengths[spline]);
					cutsBySpline[spline].Add((start, end, c, arm));
				}
			}

			for (int i = 0; i < splineCount; i++)
				cutsBySpline[i].Sort((a, b) => a.Start.CompareTo(b.Start));

			var pieces = new List<PieceRange>();
			var startChord = new float3[clusters.Count][][];
			var endChord = new float3[clusters.Count][][];
			for (int c = 0; c < clusters.Count; c++)
			{
				startChord[c] = new float3[clusters[c].ArmCount][];
				endChord[c] = new float3[clusters[c].ArmCount][];
			}

			var pieceEndsCut = new List<(int Cluster, int Arm)>();
			var pieceStartsCut = new List<(int Cluster, int Arm)>();

			for (int i = 0; i < splineCount; i++)
			{
				var cuts = cutsBySpline[i];
				float length = lengths[i];
				float cursor = 0f;

				for (int k = 0; k <= cuts.Count; k++)
				{
					float end = k < cuts.Count ? cuts[k].Start : length;
					if (end < cursor)
						end = cursor;

					bool freeStart = k == 0;
					bool freeEnd = k == cuts.Count;
					bool closed = full.SplineClosed[i] && cuts.Count == 0;

					if (end - cursor > MinPieceLength || closed)
					{
						pieces.Add(new PieceRange
						{
							SplineIndex = i,
							Start = cursor,
							End = closed ? length : end,
							Closed = closed,
							FreeStart = freeStart,
							FreeEnd = freeEnd
						});

						pieceEndsCut.Add(k < cuts.Count ? (cuts[k].Cluster, cuts[k].Arm) : (-1, -1));
						pieceStartsCut.Add(k > 0 ? (cuts[k - 1].Cluster, cuts[k - 1].Arm) : (-1, -1));
					}

					if (k < cuts.Count)
						cursor = math.max(cuts[k].End, cursor);
				}
			}

			var pieceFrames = new SweepFrame[pieces.Count][];
			var pieceClosed = new bool[pieces.Count];
			var capStartFlags = new bool[pieces.Count];
			var capEndFlags = new bool[pieces.Count];

			for (int p = 0; p < pieces.Count; p++)
			{
				ct.ThrowIfCancellationRequested();
				reportProgress();

				var piece = pieces[p];
				var spline = splines[piece.SplineIndex];
				float length = lengths[piece.SplineIndex];

				pieceFrames[p] = SweepNetworkFrames.BuildRangeFrames(spline, piece.Start, piece.End, length, piece.Start, step, maxStep, maxAngleRad, vpr, maxVertices);
				pieceClosed[p] = piece.Closed;
				capStartFlags[p] = piece.FreeStart && full.CapStartFlags[piece.SplineIndex];
				capEndFlags[p] = piece.FreeEnd && full.CapEndFlags[piece.SplineIndex];
			}

			var pieceSnapshot = new SweepSnapshot
			{
				ProfilePoints = full.ProfilePoints,
				ProfileUs = full.ProfileUs,
				ProfileSegments = full.ProfileSegments,
				ProfileClosed = full.ProfileClosed,
				Frames = pieceFrames,
				SplineClosed = pieceClosed,
				WidthLut = full.WidthLut,
				HeightLut = full.HeightLut,
				TwistLut = full.TwistLut,
				Terrain = full.Terrain,
				MaxLateralExtent = full.MaxLateralExtent,
				UvScale = full.UvScale,
				HeightOffset = full.HeightOffset,
				CapStartFlags = capStartFlags,
				CapEndFlags = capEndFlags,
				Collider = full.Collider,
				Name = full.Name
			};

			var strips = new SweepMeshData[pieces.Count];
			for (int p = 0; p < pieces.Count; p++)
			{
				ct.ThrowIfCancellationRequested();

				if (pieceFrames[p] == null || pieceFrames[p].Length < 2)
					continue;

				strips[p] = SweepMeshBuilder.Build(pieceSnapshot, p, ct, reportProgress);
				outOfBounds |= strips[p].TerrainOutOfBounds;

				var ends = pieceEndsCut[p];
				if (ends.Cluster >= 0 && strips[p].EndRing != null)
					startChord[ends.Cluster][ends.Arm] = ToFloat3(strips[p].EndRing);

				var begins = pieceStartsCut[p];
				if (begins.Cluster >= 0 && strips[p].StartRing != null)
					endChord[begins.Cluster][begins.Arm] = ToFloat3(strips[p].StartRing);
			}

			var patches = new SweepMeshData[clusters.Count];

			for (int c = 0; buildPatches && c < clusters.Count; c++)
			{
				ct.ThrowIfCancellationRequested();
				reportProgress();

				if (!valid[c])
					continue;

				if (!SweepPatchBoundaryBuilder.TryBuild(clusters[c], hits, curves, coverage, stationGuard, startChord[c], endChord[c], ct, out var loops, out string boundaryFailure))
				{
					patchFailures[c] = boundaryFailure;
					continue;
				}

				if (!SweepPatchMeshBuilder.TryBuild(loops, full.Terrain, full.HeightOffset, step, full.UvScale, ct, reportProgress, out var patch, out string meshFailure))
				{
					patchFailures[c] = meshFailure;
					continue;
				}

				patches[c] = patch;
				outOfBounds |= patch.TerrainOutOfBounds;
			}

			var hitPoints = new Vector3[hits.Count];
			for (int i = 0; i < hits.Count; i++)
				hitPoints[i] = hits[i].Point;

			var chords = new List<Vector3[]>();
			for (int c = 0; c < clusters.Count; c++)
			{
				if (!valid[c])
					continue;

				for (int arm = 0; arm < clusters[c].ArmCount; arm++)
				{
					if (!clusters[c].AbsorbedStart[arm] && startChord[c][arm] != null)
						chords.Add(new[] { (Vector3)startChord[c][arm][0], (Vector3)startChord[c][arm][1] });

					if (!clusters[c].AbsorbedEnd[arm] && endChord[c][arm] != null)
						chords.Add(new[] { (Vector3)endChord[c][arm][0], (Vector3)endChord[c][arm][1] });
				}
			}

			return new SweepPatchNetwork
			{
				Strips = strips,
				Patches = patches,
				PatchFailures = patchFailures,
				HitPoints = hitPoints,
				CutChords = chords.ToArray(),
				TerrainOutOfBounds = outOfBounds,
				ClusterCount = clusters.Count,
				HitCount = hits.Count
			};
		}

		private static bool IsFirstArm(SweepPatchCluster cluster, int arm)
		{
			for (int i = 0; i < cluster.ArmCount; i++)
			{
				if (i != arm && cluster.ArmSpline[i] == cluster.ArmSpline[arm] && cluster.CutStart[i] < cluster.CutStart[arm])
					return false;
			}
			return true;
		}

		private static bool IsLastArm(SweepPatchCluster cluster, int arm)
		{
			for (int i = 0; i < cluster.ArmCount; i++)
			{
				if (i != arm && cluster.ArmSpline[i] == cluster.ArmSpline[arm] && cluster.CutEnd[i] > cluster.CutEnd[arm])
					return false;
			}
			return true;
		}

		private static float3[] ToFloat3(Vector3[] ring)
		{
			var result = new float3[ring.Length];
			for (int i = 0; i < ring.Length; i++)
				result[i] = ring[i];
			return result;
		}

		private static float MaxLut(float[] lut)
		{
			float max = lut[0];
			for (int i = 1; i < lut.Length; i++)
				max = math.max(max, lut[i]);
			return max;
		}
	}
}
