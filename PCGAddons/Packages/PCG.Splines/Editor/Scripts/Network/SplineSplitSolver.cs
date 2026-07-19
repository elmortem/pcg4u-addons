using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public static class SplineSplitSolver
	{
		private const float MinPieceLength = 0.01f;
		private const float MergeEps = 0.01f;
		private const float KnotEps = 1e-4f;
		private const float SpanEps = 1e-4f;

		public static SplineSplitResult Solve(SplineSnapshot[] snapshots, List<SplineCut> topologyCuts, List<float3> points, float snapDistance, CancellationToken ct, Action reportProgress)
		{
			var result = new SplineSplitResult
			{
				Pieces = new List<List<KnotInstruction>>[snapshots.Length]
			};

			for (int i = 0; i < snapshots.Length; i++)
			{
				if ((i & 63) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress?.Invoke();
				}

				var snap = snapshots[i];
				if (snap == null)
				{
					result.Pieces[i] = null;
					continue;
				}

				if (snap.HasEmbeddedData)
					result.EmbeddedDataWarning = true;

				var invalid = false;
				var cuts = CollectCuts(snap, i, topologyCuts, points, snapDistance, ref invalid);
				if (invalid)
					result.InvalidValues = true;

				if (cuts.Count == 0)
				{
					result.Pieces[i] = null;
					continue;
				}

				var normalized = snap.Closed ? NormalizeClosed(snap, cuts) : NormalizeOpen(snap, cuts);
				if (normalized.Count == 0)
				{
					result.Pieces[i] = null;
					continue;
				}

				result.Pieces[i] = snap.Closed ? BuildClosedPieces(snap, normalized) : BuildOpenPieces(snap, normalized);
			}

			return result;
		}

		private static List<CutParam> CollectCuts(SplineSnapshot snap, int splineIndex, List<SplineCut> topologyCuts, List<float3> points, float snapDistance, ref bool invalid)
		{
			var cuts = new List<CutParam>();

			if (topologyCuts != null)
			{
				for (int c = 0; c < topologyCuts.Count; c++)
				{
					var cut = topologyCuts[c];
					if (cut.SplineIndex != splineIndex)
						continue;
					if (cut.CurveIndex < 0 || cut.CurveIndex >= snap.CurveCount)
						continue;
					if (!IsFinite(cut.CurveT))
					{
						invalid = true;
						continue;
					}

					var t = math.clamp(cut.CurveT, 0f, 1f);
					var distance = snap.PrefixLengths[cut.CurveIndex] + SplineNetworkMath.PartialLength(snap.Curves[cut.CurveIndex], t);
					cuts.Add(new CutParam { CurveIndex = cut.CurveIndex, T = t, Distance = distance });
				}
			}

			if (points != null && snapDistance > 0f)
			{
				var snapSq = snapDistance * snapDistance;
				for (int p = 0; p < points.Count; p++)
				{
					var point = points[p];
					if (!IsFinite(point))
					{
						invalid = true;
						continue;
					}

					NearestOnSpline(snap, point, out var curveIndex, out var t, out var distSq);
					if (distSq > snapSq)
						continue;

					var distance = snap.PrefixLengths[curveIndex] + SplineNetworkMath.PartialLength(snap.Curves[curveIndex], t);
					cuts.Add(new CutParam { CurveIndex = curveIndex, T = t, Distance = distance });
				}
			}

			return cuts;
		}

		private static void NearestOnSpline(SplineSnapshot snap, float3 point, out int curveIndex, out float t, out float distSq)
		{
			curveIndex = 0;
			t = 0f;
			distSq = float.PositiveInfinity;

			for (int ci = 0; ci < snap.CurveCount; ci++)
			{
				var curve = snap.Curves[ci];
				var res = (int)math.clamp(math.ceil(snap.CurveLengths[ci] / 0.5f), 8f, 64f);
				for (int s = 0; s <= res; s++)
				{
					var tt = (float)s / res;
					var d = math.distancesq(CurveUtility.EvaluatePosition(curve, tt), point);
					if (d < distSq)
					{
						distSq = d;
						curveIndex = ci;
						t = tt;
					}
				}
			}

			var best = snap.Curves[curveIndex];
			var res2 = (int)math.clamp(math.ceil(snap.CurveLengths[curveIndex] / 0.5f), 8f, 64f);
			var half = 1f / res2;
			var lo = math.max(0f, t - half);
			var hi = math.min(1f, t + half);

			for (int i = 0; i < 40; i++)
			{
				var m1 = lo + (hi - lo) / 3f;
				var m2 = hi - (hi - lo) / 3f;
				var d1 = math.distancesq(CurveUtility.EvaluatePosition(best, m1), point);
				var d2 = math.distancesq(CurveUtility.EvaluatePosition(best, m2), point);
				if (d1 < d2)
					hi = m2;
				else
					lo = m1;
			}

			t = math.clamp((lo + hi) * 0.5f, 0f, 1f);
			distSq = math.distancesq(CurveUtility.EvaluatePosition(best, t), point);
		}

		private static List<CutParam> NormalizeOpen(SplineSnapshot snap, List<CutParam> cuts)
		{
			var length = snap.Length;
			var filtered = new List<CutParam>(cuts.Count);
			for (int i = 0; i < cuts.Count; i++)
			{
				var cut = cuts[i];
				if (cut.Distance > MergeEps && cut.Distance < length - MergeEps)
					filtered.Add(cut);
			}

			filtered.Sort((a, b) => a.Distance.CompareTo(b.Distance));

			var result = new List<CutParam>(filtered.Count);
			var i2 = 0;
			while (i2 < filtered.Count)
			{
				var sum = filtered[i2].Distance;
				var count = 1;
				var last = filtered[i2].Distance;

				var k = i2 + 1;
				while (k < filtered.Count && filtered[k].Distance - last <= MergeEps)
				{
					sum += filtered[k].Distance;
					last = filtered[k].Distance;
					count++;
					k++;
				}

				if (count == 1)
				{
					result.Add(filtered[i2]);
				}
				else
				{
					var avg = sum / count;
					MapDistance(snap, avg, out var ci, out var t);
					result.Add(new CutParam { CurveIndex = ci, T = t, Distance = avg });
				}

				i2 = k;
			}

			return result;
		}

		private static List<CutParam> NormalizeClosed(SplineSnapshot snap, List<CutParam> cuts)
		{
			var length = snap.Length;
			var sorted = new List<CutParam>(cuts.Count);
			for (int i = 0; i < cuts.Count; i++)
			{
				var cut = cuts[i];
				var d = cut.Distance % length;
				if (d < 0f)
					d += length;
				cut.Distance = d;
				sorted.Add(cut);
			}

			sorted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
			if (sorted.Count == 0)
				return sorted;

			var clusters = new List<List<CutParam>>();
			var current = new List<CutParam> { sorted[0] };
			for (int i = 1; i < sorted.Count; i++)
			{
				if (sorted[i].Distance - current[current.Count - 1].Distance <= MergeEps)
				{
					current.Add(sorted[i]);
				}
				else
				{
					clusters.Add(current);
					current = new List<CutParam> { sorted[i] };
				}
			}
			clusters.Add(current);

			if (clusters.Count > 1)
			{
				var first = clusters[0];
				var last = clusters[clusters.Count - 1];
				var circular = (length - last[last.Count - 1].Distance) + first[0].Distance;
				if (circular <= MergeEps)
				{
					first.AddRange(last);
					clusters.RemoveAt(clusters.Count - 1);
				}
			}

			var result = new List<CutParam>(clusters.Count);
			for (int c = 0; c < clusters.Count; c++)
			{
				var cluster = clusters[c];
				var canonical = cluster[0];
				for (int m = 1; m < cluster.Count; m++)
				{
					if (cluster[m].Distance < canonical.Distance)
						canonical = cluster[m];
				}
				result.Add(canonical);
			}

			result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
			return result;
		}

		private static List<List<KnotInstruction>> BuildOpenPieces(SplineSnapshot snap, List<CutParam> cuts)
		{
			var curveCount = snap.CurveCount;
			var vertices = new List<SplitVertex>(curveCount + 1 + cuts.Count);

			for (int j = 0; j <= curveCount; j++)
			{
				vertices.Add(new SplitVertex { G = j, IsKnot = true, KnotIndex = j, IsCut = false });
			}

			for (int c = 0; c < cuts.Count; c++)
			{
				var g = cuts[c].CurveIndex + cuts[c].T;
				var ji = (int)math.round(g);
				if (math.abs(g - ji) < KnotEps && ji >= 0 && ji <= curveCount)
				{
					var v = vertices[ji];
					v.IsCut = true;
					vertices[ji] = v;
				}
				else
				{
					vertices.Add(new SplitVertex { G = g, IsKnot = false, KnotIndex = -1, IsCut = true });
				}
			}

			vertices.Sort((a, b) => a.G.CompareTo(b.G));
			return BuildPieces(snap, vertices, false);
		}

		private static List<List<KnotInstruction>> BuildClosedPieces(SplineSnapshot snap, List<CutParam> cuts)
		{
			var curveCount = snap.CurveCount;
			var vertices = new List<SplitVertex>(curveCount + cuts.Count);

			for (int j = 0; j < curveCount; j++)
			{
				vertices.Add(new SplitVertex { G = j, IsKnot = true, KnotIndex = j, IsCut = false });
			}

			for (int c = 0; c < cuts.Count; c++)
			{
				var g = cuts[c].CurveIndex + cuts[c].T;
				var ji = ((int)math.round(g)) % curveCount;
				if (math.abs(g - math.round(g)) < KnotEps && ji >= 0 && ji < curveCount)
				{
					var v = vertices[ji];
					v.IsCut = true;
					vertices[ji] = v;
				}
				else
				{
					vertices.Add(new SplitVertex { G = g, IsKnot = false, KnotIndex = -1, IsCut = true });
				}
			}

			vertices.Sort((a, b) => a.G.CompareTo(b.G));

			var firstCut = -1;
			for (int i = 0; i < vertices.Count; i++)
			{
				if (vertices[i].IsCut)
				{
					firstCut = i;
					break;
				}
			}

			if (firstCut < 0)
				return new List<List<KnotInstruction>>();

			var count = vertices.Count;
			var walk = new List<SplitVertex>(count + 1);
			for (int k = 0; k <= count; k++)
			{
				var idx = (firstCut + k) % count;
				var v = vertices[idx];
				if (firstCut + k >= count)
					v.G += curveCount;
				walk.Add(v);
			}

			return BuildPieces(snap, walk, true);
		}

		private static List<List<KnotInstruction>> BuildPieces(SplineSnapshot snap, List<SplitVertex> vertices, bool closed)
		{
			var curveCount = snap.CurveCount;
			var pieces = new List<List<KnotInstruction>>();

			var pieceVerts = new List<SplitVertex> { vertices[0] };
			var pieceSpans = new List<(BezierCurve curve, bool intact)>();

			for (int a = 0; a < vertices.Count - 1; a++)
			{
				var va = vertices[a];
				var vb = vertices[a + 1];
				var floorG = (int)math.floor((va.G + vb.G) * 0.5f);
				var curveIndex = closed ? ((floorG % curveCount) + curveCount) % curveCount : math.clamp(floorG, 0, curveCount - 1);

				var t0 = math.clamp(va.G - floorG, 0f, 1f);
				var t1 = math.clamp(vb.G - floorG, 0f, 1f);
				var sub = SplineNetworkMath.SubCurve(snap.Curves[curveIndex], t0, t1);
				var intact = t0 <= SpanEps && t1 >= 1f - SpanEps;

				pieceSpans.Add((sub, intact));
				pieceVerts.Add(vb);

				if (vb.IsCut)
				{
					var built = BuildPieceKnots(snap, pieceVerts, pieceSpans);
					if (built != null)
						pieces.Add(built);

					pieceVerts = new List<SplitVertex> { vb };
					pieceSpans = new List<(BezierCurve curve, bool intact)>();
				}
			}

			if (pieceSpans.Count > 0)
			{
				var built = BuildPieceKnots(snap, pieceVerts, pieceSpans);
				if (built != null)
					pieces.Add(built);
			}

			return pieces;
		}

		private static List<KnotInstruction> BuildPieceKnots(SplineSnapshot snap, List<SplitVertex> verts, List<(BezierCurve curve, bool intact)> spans)
		{
			var length = 0f;
			for (int s = 0; s < spans.Count; s++)
				length += CurveUtility.CalculateLength(spans[s].curve);

			if (length < MinPieceLength)
				return null;

			var m = verts.Count;
			var knots = new List<KnotInstruction>(m);

			for (int i = 0; i < m; i++)
			{
				var v = verts[i];
				var hasIn = i > 0;
				var hasOut = i < m - 1;
				var inSpan = hasIn ? spans[i - 1] : default;
				var outSpan = hasOut ? spans[i] : default;

				var keep = v.IsKnot && !v.IsCut && (!hasIn || inSpan.intact) && (!hasOut || outSpan.intact);
				if (keep)
				{
					knots.Add(new KnotInstruction
					{
						Knot = snap.Knots[v.KnotIndex],
						Mode = snap.Modes[v.KnotIndex],
						Tension = snap.Tensions[v.KnotIndex]
					});
					continue;
				}

				var rotation = v.IsKnot ? snap.Knots[v.KnotIndex].Rotation : quaternion.identity;
				var position = hasIn ? inSpan.curve.P3 : outSpan.curve.P0;
				if (v.IsKnot)
					position = snap.Knots[v.KnotIndex].Position;

				var worldIn = hasIn ? inSpan.curve.P2 - inSpan.curve.P3 : float3.zero;
				var worldOut = hasOut ? outSpan.curve.P1 - outSpan.curve.P0 : float3.zero;
				var inverse = math.inverse(rotation);

				knots.Add(new KnotInstruction
				{
					Knot = new BezierKnot(position, math.rotate(inverse, worldIn), math.rotate(inverse, worldOut), rotation),
					Mode = TangentMode.Broken,
					Tension = 0f
				});
			}

			return knots;
		}

		private static void MapDistance(SplineSnapshot snap, float distance, out int curveIndex, out float t)
		{
			var count = snap.CurveCount;
			for (int ci = 0; ci < count; ci++)
			{
				var end = snap.PrefixLengths[ci] + snap.CurveLengths[ci];
				if (distance <= end || ci == count - 1)
				{
					var local = distance - snap.PrefixLengths[ci];
					t = math.clamp(CurveUtility.GetDistanceToInterpolation(snap.Curves[ci], local), 0f, 1f);
					curveIndex = ci;
					return;
				}
			}

			curveIndex = math.max(0, count - 1);
			t = 1f;
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

		private static bool IsFinite(float3 value)
		{
			return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
		}
	}
}
