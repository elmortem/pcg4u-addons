using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Splines;
using PCG.Splines.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Mazes.Splines
{
	public class GraphToSplineNodeExecutor : PcgAsyncPreviewNodeExecutor<GraphToSplineNode>
	{
		private const string WeightAttribute = "weight";

		public PcgOutput<PcgSplineSet> Splines;

		public override bool IsEmpty => Splines.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Splines.Value = new();

			var inputGraph = GetInputValue(nameof(Data.Graph), Data.Graph);
			if (inputGraph == null)
				return;

			var nodeIndices = new Dictionary<GraphNode, int>(inputGraph.Nodes.Count);
			for (int i = 0; i < inputGraph.Nodes.Count; i++)
			{
				nodeIndices[inputGraph.Nodes[i]] = i;
			}

			var startJunctions = new List<int>(inputGraph.Edges.Count);
			var endJunctions = new List<int>(inputGraph.Edges.Count);
			var weights = new List<float>(inputGraph.Edges.Count);

			using (var scope = OperationScope.Start(this))
			{
				foreach (var edge in inputGraph.Edges)
				{
					var spline = new Spline();
					var knot = new BezierKnot(new float3(edge.Node1.Point.x, 0f, edge.Node1.Point.y), new float3(),
						new float3());
					spline.Add(knot, Data.AutoSmooth ? TangentMode.AutoSmooth : TangentMode.Broken);
					knot = new BezierKnot(new float3(edge.Node2.Point.x, 0f, edge.Node2.Point.y), new float3(),
						new float3());
					spline.Add(knot, Data.AutoSmooth ? TangentMode.AutoSmooth : TangentMode.Broken);

					Splines.Value.Add(spline);
					startJunctions.Add(nodeIndices.TryGetValue(edge.Node1, out int start) ? start : -1);
					endJunctions.Add(nodeIndices.TryGetValue(edge.Node2, out int end) ? end : -1);
					weights.Add(edge.Weight);

					await scope.Step(ct: ct);
				}
			}

			var sourceColumn = Splines.Value.Attributes.EnsureColumn<int>(SplineAttributes.SourceSplineIndex);
			var startColumn = Splines.Value.Attributes.EnsureColumn<int>(SplineAttributes.StartJunction);
			var endColumn = Splines.Value.Attributes.EnsureColumn<int>(SplineAttributes.EndJunction);
			var weightColumn = Splines.Value.Attributes.EnsureColumn<float>(WeightAttribute);
			for (int i = 0; i < Splines.Value.Count; i++)
			{
				sourceColumn.Values[i] = i;
				startColumn.Values[i] = startJunctions[i];
				endColumn.Values[i] = endJunctions[i];
				weightColumn.Values[i] = weights[i];
			}
		}

		public override void DrawPreview(Transform transform)
		{
			if (Splines.Value == null)
				return;

			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Splines.Value.Splines, transform);
		}
	}
}
