using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Splines.Tools;
using PCG.Splines.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public sealed class SplineWidthNodeExecutor : PcgAsyncPreviewNodeExecutor<SplineWidthNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null || Results.Value.Count == 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<Spline>();
			var width = math.max(0.01f, GetInputValue(nameof(Data.Width), Data.Width));
			var inputs = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (inputs == null)
				return;

			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in inputs)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline == null || spline.Count < 2)
							continue;

						var copy = SplineCopyUtility.CopySpline(spline);
						SplineWidthUtility.SetConstant(copy, width);
						Results.Value.Add(copy);
						await scope.Step(ct: ct);
					}
				}
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var options = GetGizmosOptions();
			Gizmos.color = options.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
		}
	}
}
