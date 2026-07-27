using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Splines.Tools;
using PCG.Splines.Utilities;
using PCG.Utilities;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class ResampleSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<ResampleSplinesNode>
	{
		public PcgOutput<PcgSplineSet> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new PcgSplineSet();

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

					for (int s = 0; s < splines.Splines.Count; s++)
					{
						var spline = splines.Splines[s];
						if (spline.Count <= 1)
							continue;

						Results.Value.AppendFrom(splines, s, await SplineResampleUtility.ResampleAsync(spline, step, scope, ct));
					}
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
