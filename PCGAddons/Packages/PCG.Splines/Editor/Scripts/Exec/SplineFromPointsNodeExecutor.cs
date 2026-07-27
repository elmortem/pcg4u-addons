using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Splines.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Utilities;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.Splines
{
	public class SplineFromPointsNodeExecutor : PcgAsyncPreviewNodeExecutor<SplineFromPointsNode>
	{
		public PcgOutput<PcgSplineSet> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new PcgSplineSet();

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			using (var scope = OperationScope.Start(this))
			{
				foreach (PcgPointCloud points in pointsList)
				{
					if (points == null || points.Count <= 1)
						continue;

					var spline = new Spline
					{
						Closed = Data.Closed
					};

					foreach (var point in points)
					{
						spline.Add(new BezierKnot(point.Position, float3.zero, float3.zero), TangentMode.AutoSmooth);
						await scope.Step(ct: ct);
					}

					Results.Value.Add(spline);
				}
			}
		}

		public override void DrawPreview(Transform transform)
		{
			if (Results.Value == null)
				return;

			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value.Splines, transform);
		}
	}
}
