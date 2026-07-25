using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Mazes.Graphs;
using PCG.Polygons.Convert;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Polygons
{
	public sealed class SplinesToGraphNodeExecutor : PcgAsyncPreviewNodeExecutor<SplinesToGraphNode>
	{
		public PcgOutput<Graph> Graph;

		public override bool IsEmpty => Graph.Value == null || Graph.Value.Edges.Count == 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var graph = new Graph();
			var inputs = GetInputValues(nameof(Data.Splines), Data.Splines);
			var maxSegmentLength = math.max(0.05f, GetInputValue(nameof(Data.MaxSegmentLength), Data.MaxSegmentLength));
			var mergeDistance = math.max(0.0001f, GetInputValue(nameof(Data.MergeDistance), Data.MergeDistance));
			var nodes = new Dictionary<int2, GraphNode>();

			using (var scope = OperationScope.Start(this))
			{
				if (inputs != null)
				{
					foreach (var splines in inputs)
					{
						if (splines == null)
							continue;

						foreach (var spline in splines)
						{
							if (spline == null || spline.Count < 2)
								continue;

							float length = spline.GetLength();
							int segments = math.max(spline.Closed ? 3 : 1, (int)math.ceil(length / maxSegmentLength));
							int sampleCount = spline.Closed ? segments : segments + 1;
							var path = new GraphNode[sampleCount];

							for (int i = 0; i < sampleCount; i++)
							{
								float t = (float)i / segments;
								float3 point = spline.EvaluatePosition(t);
								path[i] = GetNode(graph, nodes, new Vector2(point.x, point.z), mergeDistance);
								await scope.Step(ct: ct);
							}

							for (int i = 0; i + 1 < sampleCount; i++)
								AddEdge(graph, path[i], path[i + 1]);

							if (spline.Closed)
								AddEdge(graph, path[sampleCount - 1], path[0]);
						}
					}
				}
			}

			Graph.Value = graph;
		}

		private static GraphNode GetNode(Graph graph, Dictionary<int2, GraphNode> nodes, Vector2 point, float mergeDistance)
		{
			var key = new int2((int)math.round(point.x / mergeDistance), (int)math.round(point.y / mergeDistance));
			if (nodes.TryGetValue(key, out var node))
				return node;

			node = new GraphNode(point);
			nodes[key] = node;
			graph.Nodes.Add(node);
			return node;
		}

		private static void AddEdge(Graph graph, GraphNode a, GraphNode b)
		{
			if (a == b || graph.FindEdge(a, b) != null)
				return;

			var edge = new GraphEdge(a, b, Vector2.Distance(a.Point, b.Point));
			graph.Edges.Add(edge);
			a.Edges.Add(edge);
			b.Edges.Add(edge);
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
