using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Utilities;

namespace PCG.SelectPoints
{
	public class PointsNearSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<PointsNearSplinesNode>, IPointsCount, IShowResults
	{
		public PcgOutput<List<PointData>> Results;
		public PcgOutput<List<PointData>> NearPoints;

		private readonly List<float3> _pointsCache = new();

		public override bool IsEmpty => Results.Value == null || NearPoints.Value == null;
		public int PointsCount => ShowResults ? Results.Value?.Count ?? 0 : NearPoints.Value?.Count ?? 0;
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var distance = GetInputValue(nameof(Data.Distance), Data.Distance);
			if (distance < 0.0001f)
				return;

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return;

			_pointsCache.Clear();

			int totalCount = pointsList.TotalCount();
			var results = Results.Rent(totalCount);
			var nearPoints = NearPoints.Rent(totalCount / 10 + 10);
			using (var scope = OperationScope.Start(this))
			{
				foreach (var points in pointsList)
				{
					if (points == null)
						continue;

					foreach (var point in points)
					{
						if (CheckNearSpline(point, splinesList, distance))
							nearPoints.Add(point);
						else
							results.Add(point);

						await scope.Step(ct: ct);
					}
				}
			}
		}

		private bool CheckNearSpline(PointData point, List<Spline>[] splinesList, float distance)
		{
			if (_pointsCache.Count <= 0)
			{
				foreach (var splines in splinesList)
				{
					if (splines == null || splines.Count <= 0)
						continue;

					foreach (var spline in splines)
					{
						var splineLen = spline.GetLength();
						var count = Mathf.RoundToInt(splineLen / distance * 1.5f) + 2;
						var step = 1f / count;

						for (int i = 0; i <= count; i++)
						{
							_pointsCache.Add(spline.EvaluatePosition(i * step));
						}
					}
				}
			}

			var sqrDist = distance * distance;
			foreach (var pointCache in _pointsCache)
			{
				if (math.lengthsq(pointCache - (float3)point.Position) < sqrDist)
				{
					return true;
				}
			}

			return false;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			if (ShowResults)
				GizmosUtility.DrawPoints(Results.Value, gizmosOptions, transform);
			else
				GizmosUtility.DrawPoints(NearPoints.Value, gizmosOptions, transform);
		}
	}
}
