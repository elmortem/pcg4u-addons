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
using Random = UnityEngine.Random;

namespace PCG.CreatePoints
{
	public class SplinesSurfaceNodeExecutor : PcgAsyncPreviewNodeExecutor<SplinesSurfaceNode>, IPointsCount
	{
		public PcgOutput<List<PointData>> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		public override void OnBind()
		{
			base.OnBind();

			if (Data.Seed <= 0)
				Data.Seed = UnityEngine.Random.Range(1, int.MaxValue);
		}

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<PointData>();

			var splinesPort = GetInputPort(nameof(Data.Splines));
			var splinesList = splinesPort.GetInputValues();
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var count = GetInputValue(nameof(Data.Count), Data.Count);
			if (count <= 0)
				return;

			var offset = GetInputValue(nameof(Data.Offset), Data.Offset);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);

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

						await SplinePoints.GetPoints(scope, Results.Value, spline, Data.PointMode, count, offset, seed, ct);
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
