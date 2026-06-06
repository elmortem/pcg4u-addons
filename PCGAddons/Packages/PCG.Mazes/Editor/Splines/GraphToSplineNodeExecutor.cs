using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Splines.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Mazes.Splines
{
	public class GraphToSplineNodeExecutor : PcgAsyncPreviewNodeExecutor<GraphToSplineNode>
	{
		public PcgOutput<List<Spline>> Splines;

		public override bool IsEmpty => Splines.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Splines.Value = new();

			var inputGraph = GetInputValue(nameof(Data.Graph), Data.Graph);
			if (inputGraph == null)
				return;

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

					await scope.Step(ct: ct);
				}
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Splines.Value, transform);
		}
	}
}
