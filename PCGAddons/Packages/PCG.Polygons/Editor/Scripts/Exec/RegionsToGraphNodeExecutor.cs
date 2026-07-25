using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Mazes.Graphs;
using PCG.Polygons.Convert;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public sealed class RegionsToGraphNodeExecutor : PcgAsyncPreviewNodeExecutor<RegionsToGraphNode>
	{
		public PcgOutput<Graph> Graph;

		public override bool IsEmpty => Graph.Value == null || Graph.Value.Edges.Count == 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Regions), ct);
			var graph = new Graph();
			if (input == null)
			{
				Graph.Value = graph;
				return;
			}

			float mergeDistance = math.max(0.0001f, GetInputValue(nameof(Data.MergeDistance), Data.MergeDistance));
			Graph.Value = await PcgWorkerScheduler.RunAsync(
				() => BuildGraph(input, mergeDistance, ct),
				ct);
		}

		private static Graph BuildGraph(RegionSet input, float mergeDistance, CancellationToken ct)
		{
			var graph = new Graph();
			var nodes = new Dictionary<int2, GraphNode>();
			for (int i = 0; i < input.Regions.Count; i++)
			{
				var polygon = input.Regions[i];
				AddRing(graph, nodes, polygon.Outer, mergeDistance);
				for (int h = 0; h < polygon.Holes.Count; h++)
					AddRing(graph, nodes, polygon.Holes[h], mergeDistance);
				ct.ThrowIfCancellationRequested();
			}

			return graph;
		}

		private static void AddRing(Graph graph, Dictionary<int2, GraphNode> nodes, float2[] ring, float mergeDistance)
		{
			if (ring == null || ring.Length < 2)
				return;

			var ringNodes = new GraphNode[ring.Length];
			for (int i = 0; i < ring.Length; i++)
			{
				var point = new Vector2(ring[i].x, ring[i].y);
				var key = new int2((int)math.round(point.x / mergeDistance), (int)math.round(point.y / mergeDistance));
				if (!nodes.TryGetValue(key, out var node))
				{
					node = new GraphNode(point);
					nodes[key] = node;
					graph.Nodes.Add(node);
				}
				ringNodes[i] = node;
			}

			for (int i = 0; i < ringNodes.Length; i++)
			{
				var a = ringNodes[i];
				var b = ringNodes[(i + 1) % ringNodes.Length];
				if (a == b || graph.FindEdge(a, b) != null)
					continue;

				var edge = new GraphEdge(a, b, Vector2.Distance(a.Point, b.Point));
				graph.Edges.Add(edge);
				a.Edges.Add(edge);
				b.Edges.Add(edge);
			}
		}

		public override void DrawPreview(Transform transform)
		{
			if (Graph.Value == null)
				return;

			var options = GetGizmosOptions();
			Gizmos.color = options.Color;
			Gizmos.matrix = transform.localToWorldMatrix;
			foreach (var edge in Graph.Value.Edges)
				Gizmos.DrawLine(new Vector3(edge.Node1.Point.x, 0f, edge.Node1.Point.y), new Vector3(edge.Node2.Point.x, 0f, edge.Node2.Point.y));
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
