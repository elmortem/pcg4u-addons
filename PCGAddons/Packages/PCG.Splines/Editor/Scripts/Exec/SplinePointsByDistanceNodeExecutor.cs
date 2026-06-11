using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Splines.Surfaces;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Points;
using PCG.Utilities;
using PCG.Exec;
using PCG.GraphModel;

namespace PCG.CreatePoints
{
	public class SplinePointsByDistanceNodeExecutor : PcgAsyncPreviewNodeExecutor<SplinePointsByDistanceNode>, IPointsCount
	{
		public PcgOutput<List<PointData>> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<PointData>();

			var splinesPort = GetInputPort(nameof(Data.Splines));
			var splinesList = splinesPort.GetInputValues();
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var distance = GetInputValue(nameof(Data.Distance), Data.Distance);
			if (distance <= 0f)
				return;

			using (var scope = OperationScope.Start(this))
			{
				foreach (List<Spline> splines in splinesList)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline == null)
							continue;

						await SplinePoints.GetPointsByDistance(scope, Results.Value, spline, distance, Data.Distribute, ct);
					}
				}
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			GizmosUtility.DrawPoints(Results.Value, gizmosOptions, transform);
		}
	}
}
