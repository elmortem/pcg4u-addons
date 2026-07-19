using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public static class SplineIntersectionSolver
	{
		private const int MaxSubdivisionDepth = 12;
		private const int MaxRefineIterations = 24;
		private const int MaxCellsPerSegment = 64;
		private const float AdjacencyEps = 1e-4f;

		public static SplineIntersectionResult Solve(SplineSnapshot[] snapshots, float tolerance, float mergeDistance, float maxHeight, CancellationToken ct, Action reportProgress)
		{
			var result = new SplineIntersectionResult();
			tolerance = math.max(0.001f, tolerance);
			mergeDistance = math.max(0.001f, mergeDistance);

			var segments = new List<NetworkSegment>();
			BuildSegments(snapshots, tolerance, segments, result, ct, reportProgress);
			if (segments.Count == 0)
				return result;

			var pairs = BroadPhase(snapshots, segments, mergeDistance, ct, reportProgress);
			if (pairs.Count == 0)
				return result;

			var cuts = new List<SplineCut>();
			RefinePairs(snapshots, segments, pairs, tolerance, mergeDistance, maxHeight, cuts, result, ct, reportProgress);
			if (cuts.Count == 0)
				return result;

			var uniqueCuts = DedupCuts(cuts, mergeDistance);
			ClusterJunctions(snapshots, uniqueCuts, mergeDistance, tolerance, result);

			return result;
		}

		private static void BuildSegments(SplineSnapshot[] snapshots, float tolerance, List<NetworkSegment> baseSegments, SplineIntersectionResult result, CancellationToken ct, Action reportProgress)
		{
			var half = tolerance * 0.5f;
			var counter = 0;
			var stack = new Stack<(BezierCurve curve, float t0, float t1, int depth)>();

			for (int si = 0; si < snapshots.Length; si++)
			{
				var snap = snapshots[si];
				if (snap == null || snap.Length < 1e-4f)
					continue;

				for (int ci = 0; ci < snap.CurveCount; ci++)
				{
					if (snap.CurveLengths[ci] < 1e-4f)
						continue;

					stack.Clear();
					stack.Push((snap.Curves[ci], 0f, 1f, 0));

					while (stack.Count > 0)
					{
						if ((counter++ & 1023) == 0)
						{
							ct.ThrowIfCancellationRequested();
							reportProgress?.Invoke();
						}

						var entry = stack.Pop();
						var err = SplineNetworkMath.ChordErrorXz(entry.curve);

						if (entry.depth >= MaxSubdivisionDepth || err <= half)
						{
							if (entry.depth >= MaxSubdivisionDepth && err > half)
								result.ToleranceNotReached = true;

							AddSegment(si, ci, entry.curve, entry.t0, entry.t1, tolerance, baseSegments);
							continue;
						}

						CurveUtility.Split(entry.curve, 0.5f, out var left, out var right);
						var mid = (entry.t0 + entry.t1) * 0.5f;
						stack.Push((right, mid, entry.t1, entry.depth + 1));
						stack.Push((left, entry.t0, mid, entry.depth + 1));
					}
				}
			}
		}

		private static void AddSegment(int si, int ci, BezierCurve curve, float t0, float t1, float tolerance, List<NetworkSegment> segments)
		{
			var a = SplineNetworkMath.Xz(curve.P0);
			var b = SplineNetworkMath.Xz(curve.P3);
			var min = math.min(a, b) - tolerance;
			var max = math.max(a, b) + tolerance;

			segments.Add(new NetworkSegment
			{
				SplineIndex = si,
				CurveIndex = ci,
				T0 = t0,
				T1 = t1,
				A = a,
				B = b,
				Y0 = curve.P0.y,
				Y1 = curve.P3.y,
				Min = min,
				Max = max
			});
		}

		private static List<long> BroadPhase(SplineSnapshot[] snapshots, List<NetworkSegment> baseSegments, float mergeDistance, CancellationToken ct, Action reportProgress)
		{
			var cellSize = math.max(MedianSegmentLength(baseSegments), mergeDistance);
			if (cellSize < 1e-4f)
				cellSize = 1e-4f;

			var segments = GuardSplit(snapshots, baseSegments, cellSize);
			baseSegments.Clear();
			baseSegments.AddRange(segments);

			var grid = new Dictionary<long, List<int>>();
			for (int i = 0; i < segments.Count; i++)
			{
				if ((i & 1023) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress?.Invoke();
				}

				var seg = segments[i];
				var cx0 = (int)math.floor(seg.Min.x / cellSize);
				var cx1 = (int)math.floor(seg.Max.x / cellSize);
				var cz0 = (int)math.floor(seg.Min.y / cellSize);
				var cz1 = (int)math.floor(seg.Max.y / cellSize);

				for (int cx = cx0; cx <= cx1; cx++)
				{
					for (int cz = cz0; cz <= cz1; cz++)
					{
						var key = CellKey(cx, cz);
						if (!grid.TryGetValue(key, out var list))
						{
							list = new List<int>();
							grid[key] = list;
						}
						list.Add(i);
					}
				}
			}

			var pairSet = new HashSet<long>();
			foreach (var cell in grid.Values)
			{
				for (int a = 0; a < cell.Count; a++)
				{
					for (int b = a + 1; b < cell.Count; b++)
					{
						var i = cell[a];
						var j = cell[b];
						if (i == j)
							continue;

						var lo = math.min(i, j);
						var hi = math.max(i, j);
						if (AdjacentSameSpline(segments[lo], segments[hi], snapshots))
							continue;

						pairSet.Add(((long)lo << 32) | (uint)hi);
					}
				}
			}

			var pairs = new List<long>(pairSet);
			pairs.Sort((x, y) => ComparePairs(segments, x, y));
			return pairs;
		}

		private static List<NetworkSegment> GuardSplit(SplineSnapshot[] snapshots, List<NetworkSegment> segments, float cellSize)
		{
			var result = new List<NetworkSegment>(segments.Count);
			var queue = new Stack<NetworkSegment>();

			for (int i = segments.Count - 1; i >= 0; i--)
				queue.Push(segments[i]);

			while (queue.Count > 0)
			{
				var seg = queue.Pop();
				var cx = (int)math.floor(seg.Max.x / cellSize) - (int)math.floor(seg.Min.x / cellSize) + 1;
				var cz = (int)math.floor(seg.Max.y / cellSize) - (int)math.floor(seg.Min.y / cellSize) + 1;

				if ((long)cx * cz <= MaxCellsPerSegment || (seg.T1 - seg.T0) <= AdjacencyEps)
				{
					result.Add(seg);
					continue;
				}

				var snap = snapshots[seg.SplineIndex];
				var curve = snap.Curves[seg.CurveIndex];
				var mid = (seg.T0 + seg.T1) * 0.5f;
				var leftSub = SplineNetworkMath.SubCurve(curve, seg.T0, mid);
				var rightSub = SplineNetworkMath.SubCurve(curve, mid, seg.T1);
				var pad = math.max(seg.Max.x - math.max(seg.A.x, seg.B.x), 0.001f);

				AddGuardSegment(seg.SplineIndex, seg.CurveIndex, leftSub, seg.T0, mid, pad, queue);
				AddGuardSegment(seg.SplineIndex, seg.CurveIndex, rightSub, mid, seg.T1, pad, queue);
			}

			return result;
		}

		private static void AddGuardSegment(int si, int ci, BezierCurve curve, float t0, float t1, float pad, Stack<NetworkSegment> queue)
		{
			var a = SplineNetworkMath.Xz(curve.P0);
			var b = SplineNetworkMath.Xz(curve.P3);
			var min = math.min(a, b) - pad;
			var max = math.max(a, b) + pad;

			queue.Push(new NetworkSegment
			{
				SplineIndex = si,
				CurveIndex = ci,
				T0 = t0,
				T1 = t1,
				A = a,
				B = b,
				Y0 = curve.P0.y,
				Y1 = curve.P3.y,
				Min = min,
				Max = max
			});
		}

		private static float MedianSegmentLength(List<NetworkSegment> segments)
		{
			if (segments.Count == 0)
				return 0f;

			var lengths = new float[segments.Count];
			for (int i = 0; i < segments.Count; i++)
				lengths[i] = math.length(segments[i].B - segments[i].A);

			Array.Sort(lengths);
			return lengths[lengths.Length / 2];
		}

		private static bool AdjacentSameSpline(NetworkSegment a, NetworkSegment b, SplineSnapshot[] snapshots)
		{
			if (a.SplineIndex != b.SplineIndex)
				return false;

			var ga0 = a.CurveIndex + a.T0;
			var ga1 = a.CurveIndex + a.T1;
			var gb0 = b.CurveIndex + b.T0;
			var gb1 = b.CurveIndex + b.T1;

			if (math.abs(ga1 - gb0) < AdjacencyEps || math.abs(gb1 - ga0) < AdjacencyEps)
				return true;

			if (ga0 < gb1 - AdjacencyEps && gb0 < ga1 - AdjacencyEps)
				return true;

			var snap = snapshots[a.SplineIndex];
			if (snap.Closed)
			{
				var total = snap.CurveCount;
				if (math.abs(ga1 - total) < AdjacencyEps && math.abs(gb0) < AdjacencyEps)
					return true;
				if (math.abs(gb1 - total) < AdjacencyEps && math.abs(ga0) < AdjacencyEps)
					return true;
			}

			return false;
		}

		private static int ComparePairs(List<NetworkSegment> segments, long x, long y)
		{
			var xa = segments[(int)(x >> 32)];
			var xb = segments[(int)(x & 0xffffffff)];
			var ya = segments[(int)(y >> 32)];
			var yb = segments[(int)(y & 0xffffffff)];

			var c = CompareSegmentKey(xa, ya);
			if (c != 0)
				return c;

			return CompareSegmentKey(xb, yb);
		}

		private static int CompareSegmentKey(NetworkSegment a, NetworkSegment b)
		{
			if (a.SplineIndex != b.SplineIndex)
				return a.SplineIndex.CompareTo(b.SplineIndex);
			if (a.CurveIndex != b.CurveIndex)
				return a.CurveIndex.CompareTo(b.CurveIndex);

			var ma = (a.T0 + a.T1) * 0.5f;
			var mb = (b.T0 + b.T1) * 0.5f;
			return ma.CompareTo(mb);
		}

		private static void RefinePairs(SplineSnapshot[] snapshots, List<NetworkSegment> segments, List<long> pairs, float tolerance, float mergeDistance, float maxHeight, List<SplineCut> cuts, SplineIntersectionResult result, CancellationToken ct, Action reportProgress)
		{
			for (int p = 0; p < pairs.Count; p++)
			{
				if ((p & 1023) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress?.Invoke();
				}

				var key = pairs[p];
				var segA = segments[(int)(key >> 32)];
				var segB = segments[(int)(key & 0xffffffff)];

				var snapA = snapshots[segA.SplineIndex];
				var snapB = snapshots[segB.SplineIndex];
				var curveA = snapA.Curves[segA.CurveIndex];
				var curveB = snapB.Curves[segB.CurveIndex];

				var cross = SplineNetworkMath.SegmentsIntersectXz(segA.A, segA.B, segB.A, segB.B, out _, out _);
				if (!cross)
				{
					var distSq = SplineNetworkMath.SegmentDistanceSqXz(segA.A, segA.B, segB.A, segB.B);
					if (distSq > tolerance * tolerance)
					{
						var overlap = CollinearOverlapLength(segA, segB, tolerance);
						if (overlap > mergeDistance)
							result.CollinearOverlap = true;
						continue;
					}
				}

				if (!Refine(curveA, segA.T0, segA.T1, curveB, segB.T0, segB.T1, tolerance, out var ta, out var tb, out var posA, out var posB))
					continue;

				if (maxHeight > 0f && math.abs(posA.y - posB.y) > maxHeight)
					continue;

				AddCut(cuts, segA.SplineIndex, segA.CurveIndex, ta, snapA, posA);
				AddCut(cuts, segB.SplineIndex, segB.CurveIndex, tb, snapB, posB);
			}
		}

		private static bool Refine(BezierCurve cA, float a0, float a1, BezierCurve cB, float b0, float b1, float tolerance, out float ta, out float tb, out float3 posA, out float3 posB)
		{
			for (int iter = 0; iter < MaxRefineIterations; iter++)
			{
				var am = (a0 + a1) * 0.5f;
				var bm = (b0 + b1) * 0.5f;

				var pA0 = SplineNetworkMath.Xz(CurveUtility.EvaluatePosition(cA, a0));
				var pAm = SplineNetworkMath.Xz(CurveUtility.EvaluatePosition(cA, am));
				var pA1 = SplineNetworkMath.Xz(CurveUtility.EvaluatePosition(cA, a1));
				var pB0 = SplineNetworkMath.Xz(CurveUtility.EvaluatePosition(cB, b0));
				var pBm = SplineNetworkMath.Xz(CurveUtility.EvaluatePosition(cB, bm));
				var pB1 = SplineNetworkMath.Xz(CurveUtility.EvaluatePosition(cB, b1));

				var best = float.PositiveInfinity;
				var chooseAHigh = false;
				var chooseBHigh = false;

				EvaluateCombo(pA0, pAm, pB0, pBm, false, false, ref best, ref chooseAHigh, ref chooseBHigh);
				EvaluateCombo(pAm, pA1, pB0, pBm, true, false, ref best, ref chooseAHigh, ref chooseBHigh);
				EvaluateCombo(pA0, pAm, pBm, pB1, false, true, ref best, ref chooseAHigh, ref chooseBHigh);
				EvaluateCombo(pAm, pA1, pBm, pB1, true, true, ref best, ref chooseAHigh, ref chooseBHigh);

				if (chooseAHigh)
					a0 = am;
				else
					a1 = am;

				if (chooseBHigh)
					b0 = bm;
				else
					b1 = bm;

				var cxA = CurveUtility.EvaluatePosition(cA, (a0 + a1) * 0.5f);
				var cxB = CurveUtility.EvaluatePosition(cB, (b0 + b1) * 0.5f);
				if (math.length(SplineNetworkMath.Xz(cxA) - SplineNetworkMath.Xz(cxB)) <= tolerance)
					break;
			}

			ta = math.clamp((a0 + a1) * 0.5f, 0f, 1f);
			tb = math.clamp((b0 + b1) * 0.5f, 0f, 1f);
			posA = CurveUtility.EvaluatePosition(cA, ta);
			posB = CurveUtility.EvaluatePosition(cB, tb);

			return math.length(SplineNetworkMath.Xz(posA) - SplineNetworkMath.Xz(posB)) <= tolerance * 1.0001f;
		}

		private static void EvaluateCombo(float2 a0, float2 a1, float2 b0, float2 b1, bool aHigh, bool bHigh, ref float best, ref bool chooseAHigh, ref bool chooseBHigh)
		{
			var dist = SplineNetworkMath.SegmentDistanceSqXz(a0, a1, b0, b1);
			if (dist < best)
			{
				best = dist;
				chooseAHigh = aHigh;
				chooseBHigh = bHigh;
			}
		}

		private static void AddCut(List<SplineCut> cuts, int splineIndex, int curveIndex, float t, SplineSnapshot snap, float3 position)
		{
			var distance = snap.PrefixLengths[curveIndex] + SplineNetworkMath.PartialLength(snap.Curves[curveIndex], t);
			cuts.Add(new SplineCut
			{
				SplineIndex = splineIndex,
				CurveIndex = curveIndex,
				CurveT = t,
				Distance = distance,
				Position = position,
				JunctionIndex = -1
			});
		}

		private static float CollinearOverlapLength(NetworkSegment a, NetworkSegment b, float tolerance)
		{
			var dir = a.B - a.A;
			var len = math.length(dir);
			if (len < 1e-6f)
				return 0f;

			var n = dir / len;
			var perp = new float2(-n.y, n.x);
			if (math.abs(math.dot(b.A - a.A, perp)) > tolerance)
				return 0f;
			if (math.abs(math.dot(b.B - a.A, perp)) > tolerance)
				return 0f;

			var tb0 = math.dot(b.A - a.A, n);
			var tb1 = math.dot(b.B - a.A, n);
			var bmin = math.min(tb0, tb1);
			var bmax = math.max(tb0, tb1);
			var overlap = math.min(len, bmax) - math.max(0f, bmin);
			return math.max(0f, overlap);
		}

		private static List<SplineCut> DedupCuts(List<SplineCut> cuts, float mergeDistance)
		{
			cuts.Sort(CompareCutByDistance);

			var result = new List<SplineCut>(cuts.Count);
			var i = 0;
			while (i < cuts.Count)
			{
				var splineIndex = cuts[i].SplineIndex;
				var canonical = cuts[i];
				var sum = cuts[i].Position;
				var count = 1;
				var lastDistance = cuts[i].Distance;

				var k = i + 1;
				while (k < cuts.Count && cuts[k].SplineIndex == splineIndex && cuts[k].Distance - lastDistance <= mergeDistance)
				{
					sum += cuts[k].Position;
					lastDistance = cuts[k].Distance;
					count++;
					k++;
				}

				canonical.Position = sum / count;
				result.Add(canonical);
				i = k;
			}

			return result;
		}

		private static int CompareCutByDistance(SplineCut a, SplineCut b)
		{
			if (a.SplineIndex != b.SplineIndex)
				return a.SplineIndex.CompareTo(b.SplineIndex);
			return a.Distance.CompareTo(b.Distance);
		}

		private static void ClusterJunctions(SplineSnapshot[] snapshots, List<SplineCut> uniqueCuts, float mergeDistance, float tolerance, SplineIntersectionResult result)
		{
			var n = uniqueCuts.Count;
			var parent = new int[n];
			for (int i = 0; i < n; i++)
				parent[i] = i;

			var cellSize = math.max(mergeDistance, 1e-4f);
			var grid = new Dictionary<long, List<int>>();
			for (int i = 0; i < n; i++)
			{
				var xz = SplineNetworkMath.Xz(uniqueCuts[i].Position);
				var cx = (int)math.floor(xz.x / cellSize);
				var cz = (int)math.floor(xz.y / cellSize);
				var key = CellKey(cx, cz);
				if (!grid.TryGetValue(key, out var list))
				{
					list = new List<int>();
					grid[key] = list;
				}
				list.Add(i);
			}

			var unions = new List<long>();
			var mergeSq = mergeDistance * mergeDistance;
			for (int i = 0; i < n; i++)
			{
				var xz = SplineNetworkMath.Xz(uniqueCuts[i].Position);
				var cx = (int)math.floor(xz.x / cellSize);
				var cz = (int)math.floor(xz.y / cellSize);

				for (int dx = -1; dx <= 1; dx++)
				{
					for (int dz = -1; dz <= 1; dz++)
					{
						var key = CellKey(cx + dx, cz + dz);
						if (!grid.TryGetValue(key, out var list))
							continue;

						for (int t = 0; t < list.Count; t++)
						{
							var j = list[t];
							if (j <= i)
								continue;

							var other = SplineNetworkMath.Xz(uniqueCuts[j].Position);
							if (math.lengthsq(xz - other) <= mergeSq)
								unions.Add(((long)i << 32) | (uint)j);
						}
					}
				}
			}

			unions.Sort();
			for (int u = 0; u < unions.Count; u++)
			{
				var i = (int)(unions[u] >> 32);
				var j = (int)(unions[u] & 0xffffffff);
				Union(parent, i, j);
			}

			var rootToJunction = new Dictionary<int, int>();
			var junctionPositions = new List<float3>();
			var junctionValency = new List<int>();
			var cutJunction = new int[n];

			for (int i = 0; i < n; i++)
			{
				var root = Find(parent, i);
				if (!rootToJunction.TryGetValue(root, out var junctionIndex))
				{
					junctionIndex = junctionPositions.Count;
					rootToJunction[root] = junctionIndex;
					junctionPositions.Add(float3.zero);
					junctionValency.Add(0);
				}

				junctionPositions[junctionIndex] += uniqueCuts[i].Position;
				junctionValency[junctionIndex] += Branch(uniqueCuts[i], snapshots[uniqueCuts[i].SplineIndex], tolerance);
				cutJunction[i] = junctionIndex;
			}

			var counts = new int[junctionPositions.Count];
			for (int i = 0; i < n; i++)
				counts[cutJunction[i]]++;

			var junctions = new List<SplineJunction>(junctionPositions.Count);
			for (int j = 0; j < junctionPositions.Count; j++)
			{
				junctions.Add(new SplineJunction
				{
					Position = junctionPositions[j] / math.max(1, counts[j]),
					Valency = junctionValency[j]
				});
			}

			var order = new int[junctions.Count];
			for (int j = 0; j < order.Length; j++)
				order[j] = j;

			Array.Sort(order, (x, y) => CompareJunction(junctions[x], junctions[y]));

			var remap = new int[junctions.Count];
			var sortedJunctions = new List<SplineJunction>(junctions.Count);
			for (int j = 0; j < order.Length; j++)
			{
				remap[order[j]] = j;
				sortedJunctions.Add(junctions[order[j]]);
			}

			var resultCuts = new List<SplineCut>(n);
			for (int i = 0; i < n; i++)
			{
				var cut = uniqueCuts[i];
				cut.JunctionIndex = remap[cutJunction[i]];
				resultCuts.Add(cut);
			}

			resultCuts.Sort(CompareCutByDistance);

			result.Topology.Junctions = sortedJunctions;
			result.Topology.Cuts = resultCuts;
		}

		private static int Branch(SplineCut cut, SplineSnapshot snap, float tolerance)
		{
			if (snap.Closed)
				return 2;

			var eps = math.max(0.01f, tolerance);
			if (cut.Distance <= eps || cut.Distance >= snap.Length - eps)
				return 1;

			return 2;
		}

		private static int CompareJunction(SplineJunction a, SplineJunction b)
		{
			if (a.Position.x != b.Position.x)
				return a.Position.x.CompareTo(b.Position.x);
			if (a.Position.z != b.Position.z)
				return a.Position.z.CompareTo(b.Position.z);
			return a.Valency.CompareTo(b.Valency);
		}

		private static int Find(int[] parent, int i)
		{
			while (parent[i] != i)
			{
				parent[i] = parent[parent[i]];
				i = parent[i];
			}
			return i;
		}

		private static void Union(int[] parent, int a, int b)
		{
			var ra = Find(parent, a);
			var rb = Find(parent, b);
			if (ra == rb)
				return;

			if (ra < rb)
				parent[rb] = ra;
			else
				parent[ra] = rb;
		}

		private static long CellKey(int x, int z)
		{
			return ((long)x << 32) | (uint)z;
		}
	}
}
