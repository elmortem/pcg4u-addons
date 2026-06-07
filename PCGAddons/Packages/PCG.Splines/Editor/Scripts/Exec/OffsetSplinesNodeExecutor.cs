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
	public class OffsetSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<OffsetSplinesNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<Spline>();

			var offset = GetInputValue(nameof(Data.Offset), Data.Offset);
			var up = GetInputValue(nameof(Data.Up), Data.Up);

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

						var result = new Spline
						{
							Closed = spline.Closed
						};

						for (int k = 0; k < spline.Count; k++)
						{
							var t = SplineUtility.ConvertIndexUnit(spline, k, PathIndexUnit.Knot, PathIndexUnit.Normalized);
							spline.Evaluate(math.clamp(t, 0f, 1f), out _, out var tangent, out _);

							var direction = math.normalizesafe(math.cross(math.normalizesafe(tangent, new float3(0f, 0f, 1f)), (float3)up), new float3(1f, 0f, 0f));
							result.Add(new BezierKnot(spline[k].Position + direction * offset, float3.zero, float3.zero), TangentMode.AutoSmooth);

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
