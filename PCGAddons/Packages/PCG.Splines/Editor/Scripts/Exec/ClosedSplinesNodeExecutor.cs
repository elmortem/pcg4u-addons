using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Splines.Utilities;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class ClosedSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<ClosedSplinesNode>, IShowResults
	{
		public PcgOutput<List<Spline>> Results;

		public PcgOutput<List<Spline>> OpenedSplines;

		public override bool IsEmpty => Results.Value == null || OpenedSplines.Value == null;
		public bool ShowResults { get; set; } = true;

		protected override UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new();
			OpenedSplines.Value = new();

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return UniTask.CompletedTask;

			foreach (var splines in splinesList)
			{
				if (splines == null || splines.Count <= 0)
					continue;

				foreach (var spline in splines)
				{
					if (spline.Closed)
						Results.Value.Add(spline);
					else
						OpenedSplines.Value.Add(spline);
				}
			}

			return UniTask.CompletedTask;
		}

		public override void DrawPreview(Transform transform)
		{
			if (IsEmpty)
				return;

			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;

			if (ShowResults)
				SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
			else
				SplinesGizmoUtility.DrawGizmos(OpenedSplines.Value, transform);
		}
	}
}
