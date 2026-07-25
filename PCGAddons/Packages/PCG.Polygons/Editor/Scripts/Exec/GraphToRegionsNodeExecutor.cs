using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Mazes.Graphs;
using PCG.Polygons.Convert;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public sealed class GraphToRegionsNodeExecutor : PcgAsyncPreviewNodeExecutor<GraphToRegionsNode>
	{
		public PcgOutput<RegionSet> Regions;

		public override bool IsEmpty => Regions.Value == null || Regions.Value.Count == 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var graph = GetInputValue(nameof(Data.Graph), Data.Graph);
			float planeY = GetInputValue(nameof(Data.PlaneY), Data.PlaneY);
			if (graph == null || graph.Edges.Count == 0)
			{
				Regions.Value = new RegionSet { PlaneY = planeY };
				return;
			}

			float minArea = math.max(0f, GetInputValue(nameof(Data.MinArea), Data.MinArea));
			var positions = new Vector2[graph.Nodes.Count];
			var index = new Dictionary<GraphNode, int>();
			var edges = new List<int2>(graph.Edges.Count);
			using (var scope = OperationScope.Start(this))
			{
				for (int i = 0; i < graph.Nodes.Count; i++)
				{
					index[graph.Nodes[i]] = i;
					positions[i] = graph.Nodes[i].Point;
					await scope.Step(ct: ct);
				}

				foreach (var edge in graph.Edges)
				{
					if (index.TryGetValue(edge.Node1, out int a) &&
					    index.TryGetValue(edge.Node2, out int b) &&
					    a != b)
						edges.Add(new int2(a, b));
					await scope.Step(ct: ct);
				}
			}

			Regions.Value = await PcgWorkerScheduler.RunAsync(
				() => BuildRegions(positions, edges, planeY, minArea, ct),
				ct);
		}

		private static RegionSet BuildRegions(
			Vector2[] positions,
			List<int2> edges,
			float planeY,
			float minArea,
			CancellationToken ct)
		{
			var result = new RegionSet { PlaneY = planeY };
			var adjacency = new List<int>[positions.Length];
			for (int i = 0; i < adjacency.Length; i++)
				adjacency[i] = new List<int>();

			foreach (var edge in edges)
			{
				int a = edge.x;
				int b = edge.y;
				if (!adjacency[a].Contains(b))
					adjacency[a].Add(b);
				if (!adjacency[b].Contains(a))
					adjacency[b].Add(a);
			}

			for (int i = 0; i < adjacency.Length; i++)
			{
				int node = i;
				adjacency[i].Sort((a, b) =>
				{
					Vector2 origin = positions[node];
					float aa = math.atan2(positions[a].y - origin.y, positions[a].x - origin.x);
					float ab = math.atan2(positions[b].y - origin.y, positions[b].x - origin.x);
					return aa.CompareTo(ab);
				});
			}

			var visited = new HashSet<long>();
			for (int from = 0; from < adjacency.Length; from++)
			{
				for (int n = 0; n < adjacency[from].Count; n++)
				{
					ct.ThrowIfCancellationRequested();
					int to = adjacency[from][n];
					long startKey = DirectedKey(from, to);
					if (visited.Contains(startKey))
						continue;

					var ring = TraceFace(positions, adjacency, visited, from, to);
					if (ring.Count < 3)
						continue;

					float area = SignedArea(ring);
					if (area <= minArea)
						continue;

					result.AddRegion(new Polygon2D { Outer = ring.ToArray() });
				}
			}

			return result;
		}

		private static List<float2> TraceFace(Vector2[] positions, List<int>[] adjacency, HashSet<long> visited, int startFrom, int startTo)
		{
			var ring = new List<float2>();
			int from = startFrom;
			int to = startTo;
			int guard = 0;

			while (guard++ < 100000)
			{
				long key = DirectedKey(from, to);
				if (visited.Contains(key))
					break;

				visited.Add(key);
				Vector2 point = positions[from];
				ring.Add(new float2(point.x, point.y));

				var neighbours = adjacency[to];
				int reverse = neighbours.IndexOf(from);
				if (reverse < 0 || neighbours.Count == 0)
					return new List<float2>();

				int next = neighbours[(reverse - 1 + neighbours.Count) % neighbours.Count];
				from = to;
				to = next;

				if (from == startFrom && to == startTo)
					return ring;
			}

			return new List<float2>();
		}

		private static long DirectedKey(int from, int to)
		{
			return ((long)(uint)from << 32) | (uint)to;
		}

		private static float SignedArea(List<float2> ring)
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

		public override void DrawPreview(Transform transform)
		{
			var options = GetGizmosOptions();
			Gizmos.matrix = transform.localToWorldMatrix;
			RegionGizmoUtility.Draw(Regions.Value, options.Color, new Color(options.Color.r, options.Color.g, options.Color.b, options.Color.a * 0.5f));
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
