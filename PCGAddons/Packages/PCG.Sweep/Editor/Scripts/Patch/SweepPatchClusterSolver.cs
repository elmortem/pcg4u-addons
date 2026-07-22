using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal static class SweepPatchClusterSolver
	{
		internal static List<SweepPatchCluster> Solve(List<SweepBoundaryHit> hits, List<SweepBoundaryCurve> curves, int splineCount, float mergeDistance, CancellationToken ct)
		{
			var clusters = new List<SweepPatchCluster>();
			if (hits.Count == 0)
				return clusters;

			var parents = new int[hits.Count];
			for (int i = 0; i < parents.Length; i++)
				parents[i] = i;

			var perSpline = new List<(int Hit, float Station)>[splineCount];
			for (int s = 0; s < splineCount; s++)
				perSpline[s] = new List<(int, float)>();

			for (int h = 0; h < hits.Count; h++)
			{
				var hit = hits[h];
				int splineA = curves[hit.CurveA].SplineIndex;
				int splineB = curves[hit.CurveB].SplineIndex;
				perSpline[splineA].Add((h, hit.StationA));
				if (splineB != splineA)
					perSpline[splineB].Add((h, hit.StationB));
				else
					perSpline[splineA].Add((h, hit.StationB));
			}

			for (int s = 0; s < splineCount; s++)
			{
				ct.ThrowIfCancellationRequested();

				var list = perSpline[s];
				if (list.Count < 2)
					continue;

				list.Sort((a, b) => a.Station.CompareTo(b.Station));
				for (int i = 1; i < list.Count; i++)
				{
					if (list[i].Station - list[i - 1].Station < mergeDistance)
						Union(parents, list[i].Hit, list[i - 1].Hit);
				}
			}

			var byRoot = new Dictionary<int, SweepPatchCluster>();
			for (int h = 0; h < hits.Count; h++)
			{
				int root = Find(parents, h);
				if (!byRoot.TryGetValue(root, out var cluster))
				{
					cluster = new SweepPatchCluster();
					byRoot.Add(root, cluster);
					clusters.Add(cluster);
				}

				cluster.Hits.Add(h);
			}

			for (int c = 0; c < clusters.Count; c++)
			{
				var cluster = clusters[c];
				var stationsBySpline = new Dictionary<int, List<float>>();

				for (int i = 0; i < cluster.Hits.Count; i++)
				{
					var hit = hits[cluster.Hits[i]];
					Accumulate(stationsBySpline, curves[hit.CurveA].SplineIndex, hit.StationA);
					Accumulate(stationsBySpline, curves[hit.CurveB].SplineIndex, hit.StationB);
				}

				var splines = new List<int>(stationsBySpline.Keys);
				splines.Sort();

				var armSpline = new List<int>();
				var armStart = new List<float>();
				var armEnd = new List<float>();

				for (int i = 0; i < splines.Count; i++)
				{
					var stations = stationsBySpline[splines[i]];
					stations.Sort();

					float runStart = stations[0];
					float runEnd = stations[0];

					for (int s = 1; s < stations.Count; s++)
					{
						if (stations[s] - runEnd < mergeDistance)
						{
							runEnd = stations[s];
							continue;
						}

						armSpline.Add(splines[i]);
						armStart.Add(runStart);
						armEnd.Add(runEnd);
						runStart = stations[s];
						runEnd = stations[s];
					}

					armSpline.Add(splines[i]);
					armStart.Add(runStart);
					armEnd.Add(runEnd);
				}

				cluster.ArmSpline = armSpline.ToArray();
				cluster.CutStart = armStart.ToArray();
				cluster.CutEnd = armEnd.ToArray();
				cluster.AbsorbedStart = new bool[armSpline.Count];
				cluster.AbsorbedEnd = new bool[armSpline.Count];
			}

			clusters.Sort(Compare);
			for (int c = 0; c < clusters.Count; c++)
			{
				clusters[c].Index = c;
				for (int i = 0; i < clusters[c].Hits.Count; i++)
					hits[clusters[c].Hits[i]].Cluster = c;
			}

			return clusters;
		}

		private static int Compare(SweepPatchCluster a, SweepPatchCluster b)
		{
			int result = a.ArmSpline[0].CompareTo(b.ArmSpline[0]);
			if (result != 0)
				return result;

			return a.CutStart[0].CompareTo(b.CutStart[0]);
		}

		private static void Accumulate(Dictionary<int, List<float>> stations, int splineIndex, float station)
		{
			if (!stations.TryGetValue(splineIndex, out var list))
			{
				list = new List<float>();
				stations.Add(splineIndex, list);
			}
			list.Add(station);
		}

		private static int Find(int[] parents, int index)
		{
			while (parents[index] != index)
			{
				parents[index] = parents[parents[index]];
				index = parents[index];
			}
			return index;
		}

		private static void Union(int[] parents, int a, int b)
		{
			int rootA = Find(parents, a);
			int rootB = Find(parents, b);
			if (rootA == rootB)
				return;

			if (rootA < rootB)
				parents[rootB] = rootA;
			else
				parents[rootA] = rootB;
		}
	}
}
