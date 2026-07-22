using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Splines;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal static class SweepBoundaryIntersector
	{
		private const float MinCellSize = 1e-3f;
		private const int MaxCellsPerSegment = 1024;

		internal static List<SweepBoundaryHit> Intersect(List<SweepBoundaryCurve> curves, float heightTolerance, float cuspArcLimit, CancellationToken ct, Action reportProgress)
		{
			var curveOf = new List<int>();
			var segmentOf = new List<int>();
			for (int c = 0; c < curves.Count; c++)
			{
				var curve = curves[c];
				int count = curve.SegmentCount;
				for (int s = 0; s < count; s++)
				{
					if (math.distancesq(curve.Plan[s], curve.Plan[s + 1]) < 1e-14f)
						continue;

					curveOf.Add(c);
					segmentOf.Add(s);
				}
			}

			var hits = new List<SweepBoundaryHit>();
			int total = curveOf.Count;
			if (total < 2)
				return hits;

			double lengthSum = 0.0;
			for (int i = 0; i < total; i++)
			{
				var curve = curves[curveOf[i]];
				int s = segmentOf[i];
				lengthSum += math.distance(curve.Plan[s], curve.Plan[s + 1]);
			}

			float cellSize = math.max(MinCellSize, (float)(lengthSum / total) * 2f);

			var cells = new Dictionary<long, List<int>>();
			var large = new List<int>();
			for (int i = 0; i < total; i++)
			{
				var curve = curves[curveOf[i]];
				int s = segmentOf[i];
				Insert(cells, large, curve.Plan[s], curve.Plan[s + 1], cellSize, i);
			}

			var candidates = new List<int>();
			var visited = new HashSet<long>();
			int progressCounter = 0;

			for (int i = 0; i < total; i++)
			{
				var curveA = curves[curveOf[i]];
				int segA = segmentOf[i];
				float2 a0 = curveA.Plan[segA];
				float2 a1 = curveA.Plan[segA + 1];

				Collect(cells, large, a0, a1, cellSize, candidates);

				for (int k = 0; k < candidates.Count; k++)
				{
					int j = candidates[k];
					if (j <= i)
						continue;

					long pair = ((long)i << 32) | (uint)j;
					if (!visited.Add(pair))
						continue;

					progressCounter++;
					if (progressCounter % 1024 == 0)
					{
						ct.ThrowIfCancellationRequested();
						reportProgress();
					}

					var curveB = curves[curveOf[j]];
					int segB = segmentOf[j];

					if (curveOf[i] == curveOf[j] && math.abs(segA - segB) <= 1)
						continue;

					float2 b0 = curveB.Plan[segB];
					float2 b1 = curveB.Plan[segB + 1];

					if (!SplineNetworkMath.SegmentsIntersectXz(a0, a1, b0, b1, out float ta, out float tb))
						continue;

					float stationA = curveA.StationAt(segA, ta);
					float stationB = curveB.StationAt(segB, tb);

					if (curveA.SplineIndex == curveB.SplineIndex && math.abs(stationA - stationB) < cuspArcLimit)
						continue;

					float3 pa = curveA.PointAt(segA, ta);
					float3 pb = curveB.PointAt(segB, tb);
					if (math.abs(pa.y - pb.y) > heightTolerance)
						continue;

					hits.Add(new SweepBoundaryHit
					{
						CurveA = curveOf[i],
						CurveB = curveOf[j],
						SegmentA = segA,
						SegmentB = segB,
						ParamA = ta,
						ParamB = tb,
						StationA = stationA,
						StationB = stationB,
						Plan = curveA.PlanAt(segA, ta),
						Point = (pa + pb) * 0.5f,
						Cluster = -1
					});
				}
			}

			hits.Sort(Compare);
			for (int i = 0; i < hits.Count; i++)
				hits[i].Index = i;

			return hits;
		}

		private static int Compare(SweepBoundaryHit a, SweepBoundaryHit b)
		{
			int result = a.CurveA.CompareTo(b.CurveA);
			if (result != 0)
				return result;

			result = a.SegmentA.CompareTo(b.SegmentA);
			if (result != 0)
				return result;

			result = a.ParamA.CompareTo(b.ParamA);
			if (result != 0)
				return result;

			result = a.CurveB.CompareTo(b.CurveB);
			if (result != 0)
				return result;

			return a.SegmentB.CompareTo(b.SegmentB);
		}

		private static void Insert(Dictionary<long, List<int>> cells, List<int> large, float2 a, float2 b, float cellSize, int segment)
		{
			int x0 = (int)math.floor(math.min(a.x, b.x) / cellSize);
			int x1 = (int)math.floor(math.max(a.x, b.x) / cellSize);
			int y0 = (int)math.floor(math.min(a.y, b.y) / cellSize);
			int y1 = (int)math.floor(math.max(a.y, b.y) / cellSize);

			long count = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
			if (count > MaxCellsPerSegment)
			{
				large.Add(segment);
				return;
			}

			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					long key = ((long)x << 32) ^ (uint)y;
					if (!cells.TryGetValue(key, out var list))
					{
						list = new List<int>();
						cells.Add(key, list);
					}
					list.Add(segment);
				}
			}
		}

		private static void Collect(Dictionary<long, List<int>> cells, List<int> large, float2 a, float2 b, float cellSize, List<int> candidates)
		{
			candidates.Clear();
			candidates.AddRange(large);

			int x0 = (int)math.floor(math.min(a.x, b.x) / cellSize) - 1;
			int x1 = (int)math.floor(math.max(a.x, b.x) / cellSize) + 1;
			int y0 = (int)math.floor(math.min(a.y, b.y) / cellSize) - 1;
			int y1 = (int)math.floor(math.max(a.y, b.y) / cellSize) + 1;

			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					long key = ((long)x << 32) ^ (uint)y;
					if (cells.TryGetValue(key, out var list))
						candidates.AddRange(list);
				}
			}
		}
	}
}
