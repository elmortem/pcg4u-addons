using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Splines.Utilities;
using PCG.Utilities;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class FindSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<FindSplinesNode>
	{
		public PcgOutput<PcgSplineSet> Results;

		private readonly List<SplineContainer> _sources = new();

		public override bool IsEmpty => Results.Value == null;

		public override void OnBind()
		{
			base.OnBind();

			Spline.Changed -= OnSplineChanged;
			Spline.Changed += OnSplineChanged;
		}

		public override void CancelCompute()
		{
			Spline.Changed -= OnSplineChanged;
		}

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			if (Results.Value == null)
				Results.Value = new();
			else
			{
				foreach (var spline in Results.Value)
				{
					SplinesCache.ClearSplineCache(spline);
				}
				Results.Value.Clear();
			}

			foreach (var splineContainer in _sources)
			{
				foreach (var spline in splineContainer.Splines)
				{
					SplinesCache.ClearSplineCache(spline);
				}
			}
			_sources.Clear();

			if (string.IsNullOrEmpty(Data.Name) && string.IsNullOrEmpty(Data.Tag))
				return;

			using (var scope = OperationScope.Start(this))
			{
				if (!string.IsNullOrEmpty(Data.Name))
				{
					_sources.AddRange(UnityEngine.Object.FindObjectsOfType<SplineContainer>(false).Where(p => p.name == Data.Name));
					if (_sources.Count <= 0)
						return;
				}
				else if (!string.IsNullOrEmpty(Data.Tag))
				{
					_sources.AddRange(
						GameObject.FindGameObjectsWithTag(Data.Tag)
							.Select(p => p.GetComponent<SplineContainer>())
							.Where(p => p != null));
					if (_sources.Count <= 0)
						return;
				}
				else
				{
					_sources.Clear();
					return;
				}

				await scope.Step(ct: ct);

				foreach (var source in _sources)
				{
					foreach (var spline in source.Splines)
					{
						var transformedSpline = new Spline();
						transformedSpline.Closed = spline.Closed;
						for (var i = 0; i < spline.Count; ++i)
						{
							var knot = spline[i];
							var transformedKnot = new BezierKnot(
								source.transform.TransformPoint(knot.Position),
								source.transform.TransformDirection(knot.TangentIn),
								source.transform.TransformDirection(knot.TangentOut),
								source.transform.rotation * knot.Rotation
							);
							transformedSpline.Add(transformedKnot, spline.GetTangentMode(i));

							await scope.Step(ct: ct);
						}

						Results.Value.Add(transformedSpline);
					}
				}
			}
		}

		private void OnSplineChanged(Spline spline, int knotIndex, SplineModification mod)
		{
			if (_sources.Count <= 0)
			{
				NotifyChanged(true);
				return;
			}

			if (!_sources.Any(p => p.Splines.Contains(spline)))
				return;

			NotifyChanged(true);
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
