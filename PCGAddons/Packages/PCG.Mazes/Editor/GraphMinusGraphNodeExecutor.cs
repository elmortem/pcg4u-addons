using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Delone;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Mazes.Utilities;
using UnityEngine;

namespace PCG.Mazes
{
	public class GraphMinusGraphNodeExecutor : PcgAsyncPreviewNodeExecutor<GraphMinusGraphNode>
	{
		public PcgOutput<Graph> Result;

		public override bool IsEmpty => Result.Value == null;

		protected override UniTask DoComputeAsync(CancellationToken ct)
		{
			Result.Value = new Graph();

			var inputGraph = GetInputValue(nameof(Data.Graph), Data.Graph);
			if (inputGraph == null || inputGraph.Edges.Count <= 0)
				return UniTask.CompletedTask;

			var minusGraph = GetInputValue(nameof(Data.Minus), Data.Minus);
			if (minusGraph == null || minusGraph.Edges.Count <= 0)
				return UniTask.CompletedTask;

			foreach (var node in inputGraph.Nodes)
			{
				Result.Value.Nodes.Add(new GraphNode(node.Point));
			}

			foreach (var edge in inputGraph.Edges)
			{
				bool intersects = false;

				foreach (var minusEdge in minusGraph.Edges)
				{
					if (EdgesIntersect(edge, minusEdge))
					{
						intersects = true;
						break;
					}
				}

				if (!intersects)
				{
					var node1 = Result.Value.FindNode(edge.Node1.Point);
					var node2 = Result.Value.FindNode(edge.Node2.Point);

					if (node1 != null && node2 != null)
					{
						var newEdge = new GraphEdge(node1, node2, edge.Weight);
						Result.Value.Edges.Add(newEdge);
						node1.Edges.Add(newEdge);
						node2.Edges.Add(newEdge);
					}
				}
			}

			return UniTask.CompletedTask;
		}

		private bool EdgesIntersect(GraphEdge edge1, GraphEdge edge2)
		{
			return Arc.ArcIntersect(new Arc(edge1.Node1.Point, edge1.Node2.Point),
				new Arc(edge2.Node1.Point, edge2.Node2.Point));
		}

		public override void DrawPreview(Transform transform)
		{
			if (IsEmpty)
				return;

			var gizmosOptions = GetGizmosOptions();

			GraphGizmoUtility.DrawGraph(Result.Value, gizmosOptions, transform);
		}
	}
}
