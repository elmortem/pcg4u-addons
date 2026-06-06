using System.Collections.Generic;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Splines.Utilities;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class SplineNodeExecutor : PcgSyncPreviewNodeExecutor<SplineNode>
	{
		public PcgOutput<List<Spline>> Results;

		private Transform _editContainer;

		public override bool IsEmpty => Results.Value == null;

		public void SetData(IReadOnlyList<Spline> splines)
		{
			if (Results.Value == null)
				Results.Value = new();
			else
				Results.Value.Clear();

			Results.Value.AddRange(splines);

			NotifyChanged();
		}

		public void SetEditContainer(Transform container)
		{
			_editContainer = container;
		}

		protected override void DoCompute()
		{
		}

		public override void DrawPreview(Transform transform)
		{
			if (_editContainer != null)
			{
				_editContainer.transform.position = transform.position;
				_editContainer.transform.rotation = transform.rotation;
				_editContainer.transform.localScale = transform.localScale;
			}
			else
			{
				var gizmosOptions = GetGizmosOptions();

				Gizmos.color = gizmosOptions.Color;
				SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
			}
		}
	}
}
