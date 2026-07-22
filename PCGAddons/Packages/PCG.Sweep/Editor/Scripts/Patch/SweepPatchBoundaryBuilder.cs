using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal static class SweepPatchBoundaryBuilder
	{
		private const float StationEpsilon = 1e-4f;
		private const float MinLoopArea = 1e-6f;

		private sealed class Element
		{
			public int NodeA;
			public int NodeB;
			public float3[] Interior;
		}

		internal static bool TryBuild(SweepPatchCluster cluster, List<SweepBoundaryHit> hits, List<SweepBoundaryCurve> curves, SweepRibbonCoverage coverage, float stationGuard, float3[][] startChord, float3[][] endChord, CancellationToken ct, out List<SweepPatchLoop> loops, out string failure)
		{
			loops = null;
			failure = null;

			int armCount = cluster.ArmCount;
			int hitCount = cluster.Hits.Count;
			int nodeCount = hitCount + armCount * 4;

			var parents = new int[nodeCount];
			for (int i = 0; i < nodeCount; i++)
				parents[i] = i;

			for (int i = 0; i < hitCount; i++)
			{
				var hit = hits[cluster.Hits[i]];
				MergeToChord(parents, cluster, curves, hitCount, i, hit.CurveA, hit.StationA);
				MergeToChord(parents, cluster, curves, hitCount, i, hit.CurveB, hit.StationB);
			}

			var nodePositions = new float3[nodeCount];
			var nodeHasChord = new bool[nodeCount];

			for (int i = 0; i < hitCount; i++)
				nodePositions[Find(parents, i)] = hits[cluster.Hits[i]].Point;

			for (int arm = 0; arm < armCount; arm++)
			{
				for (int side = 0; side < 2; side++)
				{
					var curve = curves[cluster.ArmSpline[arm] * 2 + side];

					int startNode = Find(parents, ChordNode(hitCount, arm, 0, side));
					int endNode = Find(parents, ChordNode(hitCount, arm, 1, side));

					if (!nodeHasChord[startNode])
					{
						nodePositions[startNode] = startChord[arm] != null ? startChord[arm][side] : Evaluate(curve, cluster.CutStart[arm]);
						nodeHasChord[startNode] = true;
					}

					if (!nodeHasChord[endNode])
					{
						nodePositions[endNode] = endChord[arm] != null ? endChord[arm][side] : Evaluate(curve, cluster.CutEnd[arm]);
						nodeHasChord[endNode] = true;
					}
				}
			}

			var elements = new List<Element>();

			for (int arm = 0; arm < armCount; arm++)
			{
				if (!cluster.AbsorbedStart[arm])
					AddElement(elements, parents, ChordNode(hitCount, arm, 0, 0), ChordNode(hitCount, arm, 0, 1), Array.Empty<float3>());

				if (!cluster.AbsorbedEnd[arm])
					AddElement(elements, parents, ChordNode(hitCount, arm, 1, 0), ChordNode(hitCount, arm, 1, 1), Array.Empty<float3>());
			}

			var breaks = new List<(float Station, int Node)>();

			for (int arm = 0; arm < armCount; arm++)
			{
				ct.ThrowIfCancellationRequested();

				float cutStart = cluster.CutStart[arm];
				float cutEnd = cluster.CutEnd[arm];

				for (int side = 0; side < 2; side++)
				{
					int curveIndex = cluster.ArmSpline[arm] * 2 + side;
					var curve = curves[curveIndex];

					breaks.Clear();
					breaks.Add((cutStart, ChordNode(hitCount, arm, 0, side)));
					breaks.Add((cutEnd, ChordNode(hitCount, arm, 1, side)));

					for (int i = 0; i < hitCount; i++)
					{
						var hit = hits[cluster.Hits[i]];
						if (hit.CurveA == curveIndex)
							AddBreak(breaks, hit.StationA, i, cutStart, cutEnd);
						if (hit.CurveB == curveIndex)
							AddBreak(breaks, hit.StationB, i, cutStart, cutEnd);
					}

					breaks.Sort((a, b) => a.Station.CompareTo(b.Station));

					for (int i = 1; i < breaks.Count; i++)
					{
						float from = breaks[i - 1].Station;
						float to = breaks[i].Station;
						if (to - from < StationEpsilon)
							continue;

						float middleStation = (from + to) * 0.5f;
						float2 middle = EvaluatePlan(curve, middleStation);
						if (coverage.IsCovered(middle, curve.SplineIndex, middleStation, stationGuard))
							continue;

						AddElement(elements, parents, breaks[i - 1].Node, breaks[i].Node, CollectInterior(curve, from, to));
					}
				}
			}

			var adjacency = new List<int>[nodeCount];
			for (int e = 0; e < elements.Count; e++)
			{
				AddAdjacency(adjacency, elements[e].NodeA, e);
				AddAdjacency(adjacency, elements[e].NodeB, e);
			}

			for (int n = 0; n < nodeCount; n++)
			{
				if (adjacency[n] == null || adjacency[n].Count == 0)
					continue;

				if (adjacency[n].Count != 2)
				{
					failure = $"PatchNodeDegree-{cluster.Index}-{n}-{adjacency[n].Count}";
					return false;
				}
			}

			loops = new List<SweepPatchLoop>();
			var used = new bool[elements.Count];
			var points = new List<float3>();

			for (int seed = 0; seed < elements.Count; seed++)
			{
				if (used[seed])
					continue;

				points.Clear();

				int element = seed;
				int node = elements[seed].NodeA;
				int origin = node;
				int guard = elements.Count + 1;
				bool closedLoop = false;

				while (guard-- > 0)
				{
					used[element] = true;

					var current = elements[element];
					bool forward = current.NodeA == node;
					int next = forward ? current.NodeB : current.NodeA;

					points.Add(nodePositions[node]);
					if (forward)
					{
						for (int i = 0; i < current.Interior.Length; i++)
							points.Add(current.Interior[i]);
					}
					else
					{
						for (int i = current.Interior.Length - 1; i >= 0; i--)
							points.Add(current.Interior[i]);
					}

					node = next;
					if (node == origin)
					{
						closedLoop = true;
						break;
					}

					var incident = adjacency[node];
					int following = incident[0] == element ? incident[1] : incident[0];
					if (used[following])
						break;

					element = following;
				}

				if (!closedLoop)
				{
					failure = $"PatchLoopOpen-{cluster.Index}-{seed}";
					return false;
				}

				if (points.Count < 3)
					continue;

				var loop = BuildLoop(points);
				if (math.abs(loop.SignedArea()) < MinLoopArea)
					continue;

				loops.Add(loop);
			}

			if (loops.Count == 0)
			{
				failure = $"PatchLoopEmpty-{cluster.Index}";
				return false;
			}

			return true;
		}

		private static void AddBreak(List<(float Station, int Node)> breaks, float station, int node, float cutStart, float cutEnd)
		{
			if (station < cutStart - StationEpsilon || station > cutEnd + StationEpsilon)
				return;

			breaks.Add((math.clamp(station, cutStart, cutEnd), node));
		}

		private static void MergeToChord(int[] parents, SweepPatchCluster cluster, List<SweepBoundaryCurve> curves, int hitCount, int hitLocal, int curveIndex, float station)
		{
			var curve = curves[curveIndex];
			int arm = cluster.ArmOf(curve.SplineIndex, station, StationEpsilon);
			if (arm < 0)
				return;

			int side = curve.Side;
			if (side > 1)
				return;

			if (math.abs(station - cluster.CutStart[arm]) < StationEpsilon)
				Union(parents, hitLocal, ChordNode(hitCount, arm, 0, side));
			else if (math.abs(station - cluster.CutEnd[arm]) < StationEpsilon)
				Union(parents, hitLocal, ChordNode(hitCount, arm, 1, side));
		}

		private static void AddElement(List<Element> elements, int[] parents, int nodeA, int nodeB, float3[] interior)
		{
			int rootA = Find(parents, nodeA);
			int rootB = Find(parents, nodeB);
			if (rootA == rootB)
				return;

			elements.Add(new Element
			{
				NodeA = rootA,
				NodeB = rootB,
				Interior = interior
			});
		}

		private static SweepPatchLoop BuildLoop(List<float3> points)
		{
			var array = points.ToArray();
			var plan = new float2[array.Length];
			for (int i = 0; i < array.Length; i++)
				plan[i] = new float2(array[i].x, array[i].z);

			return new SweepPatchLoop
			{
				Points = array,
				Plan = plan
			};
		}

		private static void AddAdjacency(List<int>[] adjacency, int node, int element)
		{
			adjacency[node] ??= new List<int>(2);
			adjacency[node].Add(element);
		}

		private static int ChordNode(int hitCount, int arm, int end, int side)
		{
			return hitCount + arm * 4 + end * 2 + side;
		}

		private static float3[] CollectInterior(SweepBoundaryCurve curve, float from, float to)
		{
			var result = new List<float3>();
			for (int i = 0; i < curve.Station.Length; i++)
			{
				float station = curve.Station[i];
				if (station <= from + StationEpsilon)
					continue;
				if (station >= to - StationEpsilon)
					break;

				result.Add(curve.Points[i]);
			}
			return result.ToArray();
		}

		private static float3 Evaluate(SweepBoundaryCurve curve, float station)
		{
			curve.TryLocate(station, out int segment, out float t);
			return curve.PointAt(segment, t);
		}

		private static float2 EvaluatePlan(SweepBoundaryCurve curve, float station)
		{
			curve.TryLocate(station, out int segment, out float t);
			return curve.PlanAt(segment, t);
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
