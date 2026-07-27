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
		public PcgOutput<PcgSplineSet> Results;

		public PcgOutput<PcgSplineSet> OpenedSplines;

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

				for (int i = 0; i < splines.Splines.Count; i++)
				{
					if (splines.Splines[i].Closed)
						Results.Value.AppendFrom(splines, i);
					else
						OpenedSplines.Value.AppendFrom(splines, i);
				}
			}

			WriteClosed(Results.Value, true);
			WriteClosed(OpenedSplines.Value, false);

			return UniTask.CompletedTask;
		}

		public override void DrawPreview(Transform transform)
		{
			if (IsEmpty)
				return;

			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;

			if (ShowResults)
				SplinesGizmoUtility.DrawGizmos(Results.Value.Splines, transform);
			else
				SplinesGizmoUtility.DrawGizmos(OpenedSplines.Value.Splines, transform);
		}

		private static void WriteClosed(PcgSplineSet set, bool closed)
		{
			var column = set.Attributes.EnsureColumn<bool>(SplineAttributes.Closed);
			for (int i = 0; i < set.Count; i++)
			{
				column.Values[i] = closed;
			}
		}
	}
}
