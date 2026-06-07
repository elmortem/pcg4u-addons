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
	public class ResampleSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<ResampleSplinesNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<Spline>();

			var step = GetInputValue(nameof(Data.Step), Data.Step);

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return;

			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in splinesList)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline.Count <= 1)
							continue;

						var length = spline.GetLength();
						var steps = math.max(1, (int)math.round(length / math.max(0.0001f, step)));
						var arcStep = length / steps;
						var lastIndex = spline.Closed ? steps - 1 : steps;

						var result = new Spline
						{
							Closed = spline.Closed
						};

						for (int i = 0; i <= lastIndex; i++)
						{
							var t = SplineUtility.ConvertIndexUnit(spline, i * arcStep, PathIndexUnit.Distance, PathIndexUnit.Normalized);
							var position = spline.EvaluatePosition(math.clamp(t, 0f, 1f));
							result.Add(new BezierKnot(position, float3.zero, float3.zero), TangentMode.AutoSmooth);
							await scope.Step(ct: ct);
						}

						Results.Value.Add(result);
					}
				}
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
		}
	}
}
