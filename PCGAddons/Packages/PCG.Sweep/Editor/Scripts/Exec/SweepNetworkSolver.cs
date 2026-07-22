using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Polygons;
using PCG.Splines;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	internal static class SweepNetworkSolver
	{
		private const float MinPieceLength = 0.05f;
		private const float StripSpanEpsilon = 1e-4f;

		private sealed class ArmWork
		{
			public int Junction;
			public int Piece;
			public bool AtStart;
			public float Azimuth1;
			public float WidthMul;
			public float Setback;
			public float Distance;
		}

		internal static SplineSplitResult SolveSplit(
			SplineSnapshot[] snapshots,
			SplineNetworkTopology topology,
			CancellationToken ct,
			Action reportProgress)
		{
			if (topology == null || topology.Cuts == null || topology.Cuts.Count == 0)
				return null;

			return SplineSplitSolver.Solve(snapshots, topology.Cuts, new List<float3>(), 0f, ct, reportProgress);
		}

		internal static SweepNetworkSolveResult BuildNetwork(
			List<Spline> flatSplines,
			SplineSplitResult split,
			SplineNetworkTopology topology,
			float2[] profilePoints,
			float lateralExtent,
			float setbackScale,
			float step,
			float[] widthLut,
			float[] heightLut,
			float[] twistLut,
			bool hasTerrain,
			CancellationToken ct)
		{
			var pieceSplines = new List<Spline>();
			var pieceSource = new List<int>();
			var pieceClosed = new List<bool>();
			var pieceLength = new List<float>();
			var pieceSourceLength = new List<float>();
			var pieceStartDistance = new List<float>();
			var startJProv = new List<int>();
			var endJProv = new List<int>();

			float planHalfWidth = 1e-3f;
			for (int i = 0; i < profilePoints.Length; i++)
				planHalfWidth = math.max(planHalfWidth, math.abs(profilePoints[i].x));

			bool shortWarned = false;

			for (int i = 0; i < flatSplines.Count; i++)
			{
				var src = flatSplines[i];
				if (src == null || src.Count < 2)
					continue;

				bool srcClosed = src.Closed;
				var pieces = split != null && split.Pieces != null ? split.Pieces[i] : null;

				if (pieces == null)
				{
					float len = src.GetLength();
					if (len < MinPieceLength)
					{
						WarnShort(ref shortWarned);
						continue;
					}

					pieceSplines.Add(src);
					pieceSource.Add(i);
					pieceClosed.Add(srcClosed);
					pieceLength.Add(len);
					pieceSourceLength.Add(len);
					pieceStartDistance.Add(0f);
					startJProv.Add(-1);
					endJProv.Add(-1);

					ct.ThrowIfCancellationRequested();
					continue;
				}

				float sourceLen = src.GetLength();
				float accum = 0f;
				int count = pieces.Count;
				var incidence = split != null && split.PieceIncidence != null ? split.PieceIncidence[i] : null;
				for (int p = 0; p < count; p++)
				{
					var knots = pieces[p];
					if (knots == null || knots.Count < 2)
						continue;

					var built = new Spline { Closed = false };
					for (int k = 0; k < knots.Count; k++)
						built.Add(knots[k].Knot, knots[k].Mode, knots[k].Tension);

					float len = built.GetLength();
					float pieceStart = accum;
					accum += len;
					if (len < MinPieceLength)
					{
						WarnShort(ref shortWarned);
						continue;
					}

					pieceSplines.Add(built);
					pieceSource.Add(i);
					pieceClosed.Add(false);
					pieceLength.Add(len);
					pieceSourceLength.Add(sourceLen);
					pieceStartDistance.Add(pieceStart);
					int sJ = incidence != null && p < incidence.Count ? incidence[p].StartJunction : -1;
					int eJ = incidence != null && p < incidence.Count ? incidence[p].EndJunction : -1;
					startJProv.Add(sJ);
					endJProv.Add(eJ);

					ct.ThrowIfCancellationRequested();
				}
			}

			int pieceCount = pieceSplines.Count;
			var rangeStart = new float[pieceCount];
			var rangeEnd = new float[pieceCount];
			var freeStart = new bool[pieceCount];
			var freeEnd = new bool[pieceCount];
			var startJunction = new int[pieceCount];
			var endJunction = new int[pieceCount];

			var junctionsSrc = topology != null ? topology.Junctions : null;
			int junctionCount = junctionsSrc != null ? junctionsSrc.Count : 0;

			for (int p = 0; p < pieceCount; p++)
			{
				rangeStart[p] = 0f;
				rangeEnd[p] = pieceLength[p];

				startJunction[p] = ResolveEnd(junctionCount, startJProv[p]);
				freeStart[p] = startJunction[p] < 0;

				endJunction[p] = ResolveEnd(junctionCount, endJProv[p]);
				freeEnd[p] = endJunction[p] < 0;
			}

			var armsByJunction = new List<ArmWork>[junctionCount];
			var startArm = new ArmWork[pieceCount];
			var endArm = new ArmWork[pieceCount];
			for (int j = 0; j < junctionCount; j++)
				armsByJunction[j] = new List<ArmWork>();

			for (int p = 0; p < pieceCount; p++)
			{
				if (!freeStart[p])
				{
					var arm = new ArmWork { Junction = startJunction[p], Piece = p, AtStart = true };
					armsByJunction[startJunction[p]].Add(arm);
					startArm[p] = arm;
				}

				if (!freeEnd[p])
				{
					var arm = new ArmWork { Junction = endJunction[p], Piece = p, AtStart = false };
					armsByJunction[endJunction[p]].Add(arm);
					endArm[p] = arm;
				}
			}

			var junctionAxes = new float3[junctionCount];
			var junctionE1 = new float3[junctionCount];
			var junctionE2 = new float3[junctionCount];

			for (int j = 0; j < junctionCount; j++)
			{
				float3 axis = new float3(0f, 1f, 0f);
				float3 helper = math.abs(axis.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
				float3 e1 = math.normalize(math.cross(axis, helper));
				float3 e2 = math.cross(axis, e1);
				junctionAxes[j] = axis;
				junctionE1[j] = e1;
				junctionE2[j] = e2;

				var arms = armsByJunction[j];
				for (int a = 0; a < arms.Count; a++)
				{
					var arm = arms[a];
					int piece = arm.Piece;
					float3 outward = ArmOutward(pieceSplines[piece], pieceLength[piece], arm.AtStart, 0f);
					arm.Azimuth1 = Azimuth(outward, e1, e2);

					float sourceLen = pieceSourceLength[piece];
					float startD = pieceStartDistance[piece];
					float endD = arm.AtStart ? startD : startD + pieceLength[piece];
					float t0 = sourceLen > 1e-6f ? math.saturate(endD / sourceLen) : 0f;
					arm.WidthMul = SweepJunctionMeshBuilder.SampleLut(widthLut, t0);
				}

				arms.Sort(CompareArmWork);
				float3 junctionPosition = junctionsSrc[j].Position;
				ComputeSetbacks(arms, pieceSplines, pieceLength, pieceSourceLength, pieceStartDistance, profilePoints, planHalfWidth, widthLut, heightLut, twistLut, setbackScale, new float2(junctionPosition.x, junctionPosition.z));

				ct.ThrowIfCancellationRequested();
			}

			var originalStartJunction = (int[])startJunction.Clone();
			var originalEndJunction = (int[])endJunction.Clone();
			var componentParents = new int[junctionCount];
			for (int j = 0; j < junctionCount; j++)
				componentParents[j] = j;

			for (int p = 0; p < pieceCount; p++)
			{
				int start = originalStartJunction[p];
				int end = originalEndJunction[p];
				if (start < 0 || end < 0 || start == end)
					continue;
				float combinedSetback = startArm[p].Setback + endArm[p].Setback;
				if (combinedSetback > pieceLength[p] + StripSpanEpsilon)
					UnionComponents(componentParents, start, end);
			}

			var componentByRoot = new Dictionary<int, int>();
			var componentMemberLists = new List<List<int>>();
			var junctionComponents = new int[junctionCount];
			for (int j = 0; j < junctionCount; j++)
			{
				int root = FindComponent(componentParents, j);
				if (!componentByRoot.TryGetValue(root, out int component))
				{
					component = componentMemberLists.Count;
					componentByRoot.Add(root, component);
					componentMemberLists.Add(new List<int>());
				}
				junctionComponents[j] = component;
				componentMemberLists[component].Add(j);
			}

			for (int p = 0; p < pieceCount; p++)
			{
				if (startJunction[p] >= 0)
					startJunction[p] = junctionComponents[startJunction[p]];
				if (endJunction[p] >= 0)
					endJunction[p] = junctionComponents[endJunction[p]];
			}

			for (int p = 0; p < pieceCount; p++)
			{
				var sa = startArm[p];
				var ea = endArm[p];
				float sStart = sa != null ? sa.Setback : 0f;
				float sEnd = ea != null ? ea.Setback : 0f;
				float len = pieceLength[p];
				float remain = len - sStart - sEnd;
				bool mergedInternal = sa != null && ea != null &&
					originalStartJunction[p] != originalEndJunction[p] &&
					startJunction[p] == endJunction[p];

				if (mergedInternal || (sa != null || ea != null) && remain <= StripSpanEpsilon)
				{
					if (sa != null && ea != null)
					{
						float sum = sStart + sEnd;
						float dStar = sum > 1e-6f ? len * (sStart / sum) : len * 0.5f;
						sa.Distance = dStar;
						ea.Distance = dStar;
						rangeStart[p] = dStar;
						rangeEnd[p] = dStar;
					}
					else if (sa != null)
					{
						sa.Distance = len;
						rangeStart[p] = len;
						rangeEnd[p] = len;
					}
					else
					{
						ea.Distance = 0f;
						rangeStart[p] = 0f;
						rangeEnd[p] = 0f;
					}
				}
				else
				{
					if (sa != null)
					{
						sa.Distance = sStart;
					}
					if (ea != null)
					{
						ea.Distance = len - sEnd;
					}
					rangeStart[p] = sStart;
					rangeEnd[p] = len - sEnd;
				}
			}

			var junctions = new SweepNetworkJunction[componentMemberLists.Count];
			for (int component = 0; component < componentMemberLists.Count; component++)
			{
				List<int> members = componentMemberLists[component];
				int representative = members[0];
				float3 center = junctionsSrc[representative].Position;
				float3 axis = junctionAxes[representative];
				float3 e1 = junctionE1[representative];
				float3 e2 = junctionE2[representative];
				var arms = new List<ArmWork>();
				for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
					arms.AddRange(armsByJunction[members[memberIndex]]);

				var built = new List<SweepNetworkArm>(arms.Count);
				for (int a = 0; a < arms.Count; a++)
				{
					var work = arms[a];
					int piece = work.Piece;
					float len = pieceLength[piece];
					float sourceLen = pieceSourceLength[piece];
					float startD = pieceStartDistance[piece];
					float distance = work.Distance;
					float globalDist = startD + distance;
					float tCut = sourceLen > 1e-6f ? math.saturate(globalDist / sourceLen) : 0f;

					float widthMul = SweepJunctionMeshBuilder.SampleLut(widthLut, tCut);

					EvalFrame(pieceSplines[piece], distance, len, out float3 pos, out float3 tan, out float3 up);
					Basis(tan, up, out float3 right, out float3 upOut);
					float3 outward = work.AtStart ? math.normalizesafe(tan, new float3(0f, 0f, 1f)) : -math.normalizesafe(tan, new float3(0f, 0f, 1f));
					float3 radial = pos - center;
					radial -= math.dot(radial, axis) * axis;
					float layoutAzimuth = math.lengthsq(radial) > 1e-8f ? Azimuth(radial, e1, e2) : Azimuth(outward, e1, e2);
					SweepNetworkArmRole role = ResolveArmRole(piece, work.AtStart, rangeStart, rangeEnd, startJunction, endJunction);

					var arm = new SweepNetworkArm
					{
						SourceJunctionIndex = work.Junction,
						PieceIndex = piece,
						AtPieceStart = work.AtStart,
						Azimuth = layoutAzimuth,
						Outward = outward,
						Frame = new SweepFrame { Position = pos, Tangent = tan, Up = up, T = tCut, Distance = distance },
						Right = right,
						Up = upOut,
						WidthMul = widthMul,
						Role = role,
						Terminal = role == SweepNetworkArmRole.ExposedCap
					};
					FillEdgeDir(arm, axis);
					BuildApproach(pieceSplines[piece], len, distance, work.AtStart, sourceLen, startD, step, out arm.ApproachFrames, out arm.ApproachRights, out arm.ApproachUps);
					SweepFrame originFrame = arm.ApproachFrames[0];
					originFrame.Position = junctionsSrc[work.Junction].Position;
					arm.ApproachFrames[0] = originFrame;
					built.Add(arm);
				}

				built.Sort(CompareArms);

				junctions[component] = new SweepNetworkJunction
				{
					SourceJunctionIndices = members.ToArray(),
					Center = center,
					Axis = axis,
					E1 = e1,
					E2 = e2,
					Arms = built.ToArray()
				};
			}

			return new SweepNetworkSolveResult
			{
				PieceSplines = pieceSplines,
				RangeStart = rangeStart,
				RangeEnd = rangeEnd,
				FreeStart = freeStart,
				FreeEnd = freeEnd,
				PieceClosed = pieceClosed.ToArray(),
				SourceLength = pieceSourceLength.ToArray(),
				PieceStartDistance = pieceStartDistance.ToArray(),
				JunctionComponents = junctionComponents,
				Junctions = junctions
			};
		}

		private static void FillEdgeDir(SweepNetworkArm arm, float3 axis)
		{
			float3 planeOut = arm.Outward - math.dot(arm.Outward, axis) * axis;
			float3 fallback = math.normalizesafe(math.cross(axis, arm.Right), new float3(1f, 0f, 0f));
			arm.EdgeDir = math.normalizesafe(planeOut, fallback);
		}

		private static void WarnShort(ref bool warned)
		{
			if (warned)
				return;
			warned = true;
			Debug.LogWarning("[Sweep Spline] A piece is shorter than the minimum length and was skipped.");
		}

		private static int FindComponent(int[] parents, int value)
		{
			int root = value;
			while (parents[root] != root)
				root = parents[root];
			while (parents[value] != value)
			{
				int next = parents[value];
				parents[value] = root;
				value = next;
			}
			return root;
		}

		private static void UnionComponents(int[] parents, int first, int second)
		{
			int firstRoot = FindComponent(parents, first);
			int secondRoot = FindComponent(parents, second);
			if (firstRoot == secondRoot)
				return;
			if (firstRoot < secondRoot)
				parents[secondRoot] = firstRoot;
			else
				parents[firstRoot] = secondRoot;
		}

		private static int ResolveEnd(int junctionCount, int provenance)
		{
			return provenance >= 0 && provenance < junctionCount ? provenance : -1;
		}

		private static SweepNetworkArmRole ResolveArmRole(int piece, bool atStart, float[] rangeStart, float[] rangeEnd, int[] startJunction, int[] endJunction)
		{
			if (rangeEnd[piece] - rangeStart[piece] > StripSpanEpsilon)
				return SweepNetworkArmRole.StripSeam;

			int ownJunction = atStart ? startJunction[piece] : endJunction[piece];
			int otherJunction = atStart ? endJunction[piece] : startJunction[piece];
			if (otherJunction < 0)
				return SweepNetworkArmRole.ExposedCap;
			return otherJunction == ownJunction ? SweepNetworkArmRole.InternalAbsorbed : SweepNetworkArmRole.PatchSeam;
		}

		private static void ComputeSetbacks(List<ArmWork> arms, List<Spline> pieceSplines, List<float> pieceLength, List<float> pieceSourceLength, List<float> pieceStartDistance, float2[] profilePoints, float planHalfWidth, float[] widthLut, float[] heightLut, float[] twistLut, float setbackScale, float2 junctionCenter)
		{
			int n = arms.Count;
			if (n == 0)
				return;

			var setback = new float[n];
			var maxArmWidth = new float[n];
			for (int a = 0; a < n; a++)
			{
				maxArmWidth[a] = planHalfWidth * arms[a].WidthMul;
				setback[a] = 0.75f * maxArmWidth[a];
			}

			if (n >= 2)
			{
				var points = new List<float2>[n];
				var rights = new List<float2>[n];
				var distances = new List<float>[n];
				var widths = new List<float>[n];
				for (int a = 0; a < n; a++)
				{
					int piece = arms[a].Piece;
					SampleArm(pieceSplines[piece], pieceLength[piece], arms[a].AtStart, pieceSourceLength[piece], pieceStartDistance[piece], profilePoints, planHalfWidth, widthLut, heightLut, twistLut, out points[a], out rights[a], out distances[a], out widths[a]);
					for (int i = 0; i < widths[a].Count; i++)
						maxArmWidth[a] = math.max(maxArmWidth[a], widths[a][i]);
				}

				float scale = math.max(1f, math.max(0f, setbackScale));
				for (int a = 0; a < n; a++)
					setback[a] *= scale;

				int budget = 2;
				for (int a = 0; a < n; a++)
					budget += math.max(0, distances[a].Count - 1);

				for (int pass = 0; pass < budget; pass++)
				{
					bool advanced = false;
					for (int a = 0; a < n - 1; a++)
					{
						for (int b = a + 1; b < n; b++)
						{
							PairClearance(points[a], rights[a], distances[a], widths[a], setback[a], points[b], rights[b], distances[b], widths[b], setback[b], out float clearA, out float clearB);
							if (clearA > setback[a])
							{
								setback[a] = clearA;
								advanced = true;
							}
							if (clearB > setback[b])
							{
								setback[b] = clearB;
								advanced = true;
							}
						}
					}

					advanced |= ExposePortals(points, rights, distances, widths, setback, junctionCenter);
					advanced |= ExposeOwnSuffix(points, rights, distances, widths, setback);
					if (!advanced)
						break;
				}
			}

			for (int a = 0; a < n; a++)
			{
				float cap = math.min(pieceLength[arms[a].Piece], 64f * maxArmWidth[a]);
				arms[a].Setback = math.min(setback[a], cap);
			}
		}

		private static void SampleArm(Spline piece, float length, bool atStart, float sourceLength, float sourceStart, float2[] profilePoints, float planHalfWidth, float[] widthLut, float[] heightLut, float[] twistLut, out List<float2> points, out List<float2> rights, out List<float> distances, out List<float> widths)
		{
			points = new List<float2>();
			rights = new List<float2>();
			distances = new List<float>();
			widths = new List<float>();
			float maxWidthMul = 1e-3f;
			for (int i = 0; i < widthLut.Length; i++)
				maxWidthMul = math.max(maxWidthMul, math.abs(widthLut[i]));
			float referenceWidth = math.max(0.01f, planHalfWidth * maxWidthMul);
			float maxLength = math.min(length, 64f * referenceWidth);
			float spacing = math.max(0.05f, referenceWidth * 0.1f);
			int count = math.clamp((int)math.ceil(maxLength / spacing), 2, 4096);
			for (int i = 0; i <= count; i++)
			{
				float distance = maxLength * i / count;
				float curveDistance = atStart ? distance : length - distance;
				EvalFrame(piece, curveDistance, length, out float3 position, out float3 tangent, out _);
				float2 tangent2 = math.normalizesafe(new float2(tangent.x, tangent.z), new float2(0f, 1f));
				points.Add(new float2(position.x, position.z));
				rights.Add(new float2(tangent2.y, -tangent2.x));
				distances.Add(distance);
				float sourceT = sourceLength > 1e-6f ? math.saturate((sourceStart + curveDistance) / sourceLength) : 0f;
				float widthMul = SweepJunctionMeshBuilder.SampleLut(widthLut, sourceT);
				float heightMul = SweepJunctionMeshBuilder.SampleLut(heightLut, sourceT);
				float twist = math.radians(SweepJunctionMeshBuilder.SampleLut(twistLut, sourceT));
				float cosine = math.cos(twist);
				float sine = math.sin(twist);
				float halfWidth = 1e-3f;
				for (int p = 0; p < profilePoints.Length; p++)
				{
					float lateral = profilePoints[p].x * widthMul;
					float vertical = profilePoints[p].y * heightMul;
					halfWidth = math.max(halfWidth, math.abs(lateral * cosine - vertical * sine));
				}
				widths.Add(halfWidth);
			}
		}

		private static void PairClearance(List<float2> pointsA, List<float2> rightsA, List<float> distancesA, List<float> widthsA, float requiredA, List<float2> pointsB, List<float2> rightsB, List<float> distancesB, List<float> widthsB, float requiredB, out float clearA, out float clearB)
		{
			int indexA = FindDistanceIndex(distancesA, requiredA);
			int indexB = FindDistanceIndex(distancesB, requiredB);
			for (int guard = 0; guard < pointsA.Count + pointsB.Count; guard++)
			{
				float clearance = math.max(0.01f, math.min(widthsA[indexA], widthsB[indexB]) * 0.005f);
				bool exposedA = CrossSectionClearance(pointsA[indexA], rightsA[indexA], widthsA[indexA], pointsB, widthsB, indexB) >= clearance;
				bool exposedB = CrossSectionClearance(pointsB[indexB], rightsB[indexB], widthsB[indexB], pointsA, widthsA, indexA) >= clearance;
				if (exposedA && exposedB)
					break;
				bool advanced = false;
				if (!exposedA && indexA < pointsA.Count - 1)
				{
					indexA++;
					advanced = true;
				}
				if (!exposedB && indexB < pointsB.Count - 1)
				{
					indexB++;
					advanced = true;
				}
				if (!advanced)
					break;
			}
			clearA = distancesA[indexA];
			clearB = distancesB[indexB];
		}

		private static int FindDistanceIndex(List<float> distances, float required)
		{
			for (int i = 0; i < distances.Count; i++)
			{
				if (distances[i] >= required)
					return i;
			}
			return distances.Count - 1;
		}

		private static float CrossSectionClearance(float2 center, float2 right, float width, List<float2> polyline, List<float> widths, int startIndex)
		{
			float2 a0 = center - right * width;
			float2 a1 = center + right * width;
			float best = float.MaxValue;
			for (int i = math.clamp(startIndex, 0, polyline.Count - 1); i < polyline.Count - 1; i++)
				best = math.min(best, SegmentSegmentDistance(a0, a1, polyline[i], polyline[i + 1]) - math.max(widths[i], widths[i + 1]));
			return best;
		}

		private static bool ExposePortals(List<float2>[] points, List<float2>[] rights, List<float>[] distances, List<float>[] widths, float[] setback, float2 junctionCenter)
		{
			int armCount = points.Length;
			var indices = new int[armCount];
			for (int a = 0; a < armCount; a++)
				indices[a] = FindDistanceIndex(distances[a], setback[a]);

			Polygon2D region = BuildLayoutRegion(points, rights, widths, indices, junctionCenter);
			if (region == null)
				return false;

			bool advanced = false;
			for (int a = 0; a < armCount; a++)
			{
				int index = indices[a];
				float2 center = points[a][index];
				float2 side = rights[a][index] * widths[a][index];
				float distance = PortalBoundaryDistance(region, center - side, center + side);
				if (distance <= 0.004f || index >= points[a].Count - 1)
					continue;
				float spacing = index + 1 < distances[a].Count ? distances[a][index + 1] - distances[a][index] : 0.05f;
				int stepCount = math.max(1, (int)math.ceil(distance / math.max(spacing, 0.01f)));
				indices[a] = math.min(points[a].Count - 1, index + stepCount);
				advanced = true;
			}

			for (int a = 0; a < armCount; a++)
				setback[a] = math.max(setback[a], distances[a][indices[a]]);
			return advanced;
		}

		private static bool ExposeOwnSuffix(List<float2>[] points, List<float2>[] rights, List<float>[] distances, List<float>[] widths, float[] setback)
		{
			bool advanced = false;
			for (int arm = 0; arm < points.Length; arm++)
			{
				int index = FindDistanceIndex(distances[arm], setback[arm]);
				int original = index;
				while (index < points[arm].Count - 1 && !PortalClearOfSuffix(points[arm], rights[arm], widths[arm], index))
					index++;
				if (index == original)
					continue;
				setback[arm] = math.max(setback[arm], distances[arm][index]);
				advanced = true;
			}
			return advanced;
		}

		private static bool PortalClearOfSuffix(List<float2> points, List<float2> rights, List<float> widths, int index)
		{
			float2 portalA = points[index] - rights[index] * widths[index];
			float2 portalB = points[index] + rights[index] * widths[index];
			float clearance = math.max(0.01f, widths[index] * 0.005f);
			for (int cell = index + 1; cell < points.Count - 1; cell++)
			{
				float2 a0 = points[cell] - rights[cell] * widths[cell];
				float2 a1 = points[cell] + rights[cell] * widths[cell];
				float2 b1 = points[cell + 1] + rights[cell + 1] * widths[cell + 1];
				float2 b0 = points[cell + 1] - rights[cell + 1] * widths[cell + 1];
				if (SegmentQuadDistance(portalA, portalB, a0, a1, b1, b0) <= clearance)
					return false;
			}
			return true;
		}

		private static float SegmentQuadDistance(float2 a, float2 b, float2 q0, float2 q1, float2 q2, float2 q3)
		{
			if (PointInTriangle(a, q0, q1, q2) || PointInTriangle(a, q0, q2, q3) ||
				PointInTriangle(b, q0, q1, q2) || PointInTriangle(b, q0, q2, q3))
				return 0f;
			float distance = SegmentSegmentDistance(a, b, q0, q1);
			distance = math.min(distance, SegmentSegmentDistance(a, b, q1, q2));
			distance = math.min(distance, SegmentSegmentDistance(a, b, q2, q3));
			return math.min(distance, SegmentSegmentDistance(a, b, q3, q0));
		}

		private static bool PointInTriangle(float2 point, float2 a, float2 b, float2 c)
		{
			float ab = Cross(b - a, point - a);
			float bc = Cross(c - b, point - b);
			float ca = Cross(a - c, point - c);
			const float epsilon = 1e-6f;
			bool negative = ab < -epsilon || bc < -epsilon || ca < -epsilon;
			bool positive = ab > epsilon || bc > epsilon || ca > epsilon;
			return !(negative && positive);
		}

		private static Polygon2D BuildLayoutRegion(List<float2>[] points, List<float2>[] rights, List<float>[] widths, int[] indices, float2 junctionCenter)
		{
			var corridors = new List<Polygon2D>();
			for (int a = 0; a < points.Length; a++)
			{
				for (int i = 0; i < indices[a]; i++)
				{
					float2 a0 = points[a][i] - rights[a][i] * widths[a][i];
					float2 a1 = points[a][i] + rights[a][i] * widths[a][i];
					float2 b1 = points[a][i + 1] + rights[a][i + 1] * widths[a][i + 1];
					float2 b0 = points[a][i + 1] - rights[a][i + 1] * widths[a][i + 1];
					var ring = new[] { a0, a1, b1, b0 };
					float area = LayoutArea(ring);
					if (math.abs(area) < 1e-8f)
						continue;
					if (area < 0f)
						Array.Reverse(ring);
					corridors.Add(new Polygon2D { Outer = ring });
				}
			}
			if (corridors.Count == 0)
				return null;

			List<Polygon2D> united;
			try
			{
				united = PolygonClipper.Union(corridors, Array.Empty<Polygon2D>());
			}
			catch
			{
				return null;
			}
			if (united == null || united.Count == 0)
				return null;

			Polygon2D best = null;
			float bestArea = 0f;
			bool bestContainsCenter = false;
			for (int i = 0; i < united.Count; i++)
			{
				Polygon2D candidate = united[i];
				if (candidate?.Outer == null || candidate.Outer.Length < 3)
					continue;
				float area = math.abs(LayoutArea(candidate.Outer));
				bool containsCenter = candidate.Contains(junctionCenter);
				if (best == null || containsCenter && !bestContainsCenter || containsCenter == bestContainsCenter && area > bestArea)
				{
					best = candidate;
					bestArea = area;
					bestContainsCenter = containsCenter;
				}
			}
			return best;
		}

		private static float PortalBoundaryDistance(Polygon2D region, float2 a, float2 b)
		{
			float distance = RingDistance(region.Outer, a);
			distance = math.max(distance, RingDistance(region.Outer, b));
			for (int i = 1; i < 4; i++)
				distance = math.max(distance, RingDistance(region.Outer, math.lerp(a, b, i * 0.25f)));
			for (int h = 0; h < region.Holes.Count; h++)
			{
				float holeDistance = RingDistance(region.Holes[h], a);
				holeDistance = math.max(holeDistance, RingDistance(region.Holes[h], b));
				for (int i = 1; i < 4; i++)
					holeDistance = math.max(holeDistance, RingDistance(region.Holes[h], math.lerp(a, b, i * 0.25f)));
				distance = math.min(distance, holeDistance);
			}
			return distance;
		}

		private static float RingDistance(float2[] ring, float2 point)
		{
			float distance = float.MaxValue;
			for (int i = 0; i < ring.Length; i++)
				distance = math.min(distance, PointSegmentDistance(point, ring[i], ring[(i + 1) % ring.Length]));
			return distance;
		}

		private static float LayoutArea(IReadOnlyList<float2> ring)
		{
			float area = 0f;
			for (int i = 0; i < ring.Count; i++)
			{
				float2 a = ring[i];
				float2 b = ring[(i + 1) % ring.Count];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}

		private static float SegmentSegmentDistance(float2 a0, float2 a1, float2 b0, float2 b1)
		{
			if (SegmentsIntersect(a0, a1, b0, b1))
				return 0f;
			float distance = PointSegmentDistance(a0, b0, b1);
			distance = math.min(distance, PointSegmentDistance(a1, b0, b1));
			distance = math.min(distance, PointSegmentDistance(b0, a0, a1));
			return math.min(distance, PointSegmentDistance(b1, a0, a1));
		}

		private static bool SegmentsIntersect(float2 a0, float2 a1, float2 b0, float2 b1)
		{
			float d1 = Cross(b1 - b0, a0 - b0);
			float d2 = Cross(b1 - b0, a1 - b0);
			float d3 = Cross(a1 - a0, b0 - a0);
			float d4 = Cross(a1 - a0, b1 - a0);
			return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
		}

		private static float Cross(float2 a, float2 b)
		{
			return a.x * b.y - a.y * b.x;
		}

		private static float PointSegmentDistance(float2 point, float2 a, float2 b)
		{
			float2 ab = b - a;
			float lengthSq = math.dot(ab, ab);
			float t = lengthSq > 1e-12f ? math.saturate(math.dot(point - a, ab) / lengthSq) : 0f;
			return math.distance(point, a + t * ab);
		}

		private static int CompareArmWork(ArmWork a, ArmWork b)
		{
			int order = a.Azimuth1.CompareTo(b.Azimuth1);
			if (order != 0)
				return order;
			order = a.Piece.CompareTo(b.Piece);
			return order != 0 ? order : a.AtStart.CompareTo(b.AtStart);
		}

		private static int CompareArms(SweepNetworkArm a, SweepNetworkArm b)
		{
			int order = a.Azimuth.CompareTo(b.Azimuth);
			if (order != 0)
				return order;
			order = a.PieceIndex.CompareTo(b.PieceIndex);
			return order != 0 ? order : a.AtPieceStart.CompareTo(b.AtPieceStart);
		}

		private static void BuildApproach(Spline piece, float length, float portalDistance, bool atStart, float sourceLength, float sourceStart, float step, out SweepFrame[] frames, out float3[] rights, out float3[] ups)
		{
			float approachLength = atStart ? portalDistance : length - portalDistance;
			float spacing = math.max(0.1f, step);
			int count = math.max(2, (int)math.ceil(approachLength / spacing) + 1);
			frames = new SweepFrame[count];
			rights = new float3[count];
			ups = new float3[count];
			for (int i = 0; i < count; i++)
			{
				float t = i / (float)(count - 1);
				float distanceFromEndpoint = approachLength * t;
				float curveDistance = atStart ? distanceFromEndpoint : length - distanceFromEndpoint;
				EvalFrame(piece, curveDistance, length, out float3 position, out float3 tangent, out float3 up);
				Basis(tangent, up, out float3 right, out float3 upOut);
				float globalDistance = sourceStart + curveDistance;
				float sourceT = sourceLength > 1e-6f ? math.saturate(globalDistance / sourceLength) : 0f;
				frames[i] = new SweepFrame
				{
					Position = position,
					Tangent = tangent,
					Up = up,
					T = sourceT,
					Distance = curveDistance
				};
				rights[i] = right;
				ups[i] = upOut;
			}
		}

		private static float NormalizeGap(float gap)
		{
			while (gap < 0f)
				gap += 2f * math.PI;
			while (gap > 2f * math.PI)
				gap -= 2f * math.PI;
			return gap;
		}

		private static float NormalizeSigned(float angle)
		{
			while (angle > math.PI)
				angle -= 2f * math.PI;
			while (angle <= -math.PI)
				angle += 2f * math.PI;
			return angle;
		}

		private static float3 ArmOutward(Spline piece, float length, bool atStart, float distance)
		{
			float d = atStart ? distance : length - distance;
			EvalFrame(piece, d, length, out _, out float3 tan, out _);
			float3 dir = math.normalizesafe(tan, new float3(0f, 0f, 1f));
			return atStart ? dir : -dir;
		}

		private static void EvalFrame(Spline piece, float distance, float length, out float3 pos, out float3 tan, out float3 up)
		{
			float normalized = math.clamp(piece.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized), 0f, 1f);
			pos = piece.EvaluatePosition(normalized);
			tan = piece.EvaluateTangent(normalized);
			up = piece.EvaluateUpVector(normalized);
			if (!math.all(math.isfinite(tan)) || math.lengthsq(tan) < 1e-12f)
			{
				float probeDistance = math.max(1e-3f, length * 1e-4f);
				float beforeDistance = math.max(0f, distance - probeDistance);
				float afterDistance = math.min(length, distance + probeDistance);
				float beforeT = math.clamp(piece.ConvertIndexUnit(beforeDistance, PathIndexUnit.Distance, PathIndexUnit.Normalized), 0f, 1f);
				float afterT = math.clamp(piece.ConvertIndexUnit(afterDistance, PathIndexUnit.Distance, PathIndexUnit.Normalized), 0f, 1f);
				tan = piece.EvaluatePosition(afterT) - piece.EvaluatePosition(beforeT);
			}
			tan = math.normalizesafe(tan, new float3(0f, 0f, 1f));
			if (!math.all(math.isfinite(up)) || math.lengthsq(up) < 1e-12f)
				up = new float3(0f, 1f, 0f);
		}

		private static void Basis(float3 tangent, float3 upIn, out float3 right, out float3 up)
		{
			float3 t = math.normalizesafe(tangent, new float3(0f, 0f, 1f));
			right = math.normalizesafe(math.cross(upIn, t), new float3(1f, 0f, 0f));
			up = math.cross(t, right);
		}

		private static float Azimuth(float3 outward, float3 e1, float3 e2)
		{
			return math.atan2(math.dot(outward, e2), math.dot(outward, e1));
		}

	}
}
