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
	public class SmoothSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<SmoothSplinesNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<Spline>();

			var iterationsInput = GetInputValue(nameof(Data.Iterations), Data.Iterations);
			var strengthInput = GetInputValue(nameof(Data.Strength), Data.Strength);

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
						if (spline.Count <= 2)
						{
							Results.Value.Add(spline);
							continue;
						}

						var positions = new List<float3>(spline.Count);
						for (int k = 0; k < spline.Count; k++)
							positions.Add(spline[k].Position);

						var strength = math.clamp(strengthInput, 0f, 1f);
						for (int iter = 0; iter < math.max(0, iterationsInput); iter++)
						{
							var source = new List<float3>(positions);
							var first = spline.Closed ? 0 : 1;
							var last = spline.Closed ? positions.Count - 1 : positions.Count - 2;

							for (int k = first; k <= last; k++)
							{
								var prev = source[(k - 1 + source.Count) % source.Count];
								var next = source[(k + 1) % source.Count];
								positions[k] = math.lerp(source[k], (prev + next) * 0.5f, strength);
							}

							await scope.Step(ct: ct);
						}

						var result = new Spline
						{
							Closed = spline.Closed
						};
						foreach (var position in positions)
							result.Add(new BezierKnot(position, float3.zero, float3.zero), TangentMode.AutoSmooth);

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
