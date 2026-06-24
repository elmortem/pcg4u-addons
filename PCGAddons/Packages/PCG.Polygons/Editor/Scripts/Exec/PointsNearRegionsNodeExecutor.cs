using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Polygons;
using PCG.Utilities;

namespace PCG.SelectPoints
{
	public class PointsNearRegionsNodeExecutor : PcgAsyncPreviewNodeExecutor<PointsNearRegionsNode>, IPointsCount, IShowResults
	{
		public PcgOutput<List<PointData>> Results;
		public PcgOutput<List<PointData>> NearPoints;

		private readonly List<float2> _boundsMin = new();
		private readonly List<float2> _boundsMax = new();

		public override bool IsEmpty => Results.Value == null || NearPoints.Value == null;
		public int PointsCount => ShowResults ? Results.Value?.Count ?? 0 : NearPoints.Value?.Count ?? 0;
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var radius = GetInputValue(nameof(Data.Radius), Data.Radius);
			if (radius < 0.0001f)
				return;

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var regions = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Regions), ct);
			var hasRegions = regions != null && regions.Count > 0;

			_boundsMin.Clear();
			_boundsMax.Clear();

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
						if (hasRegions && CheckNearRegion(point, regions, radius))
							nearPoints.Add(point);
						else
							results.Add(point);

						await scope.Step(ct: ct);
					}
				}
			}
		}

		private bool CheckNearRegion(PointData point, RegionSet regions, float radius)
		{
			if (_boundsMin.Count <= 0)
			{
				for (int i = 0; i < regions.Regions.Count; i++)
				{
					regions.Regions[i].GetBounds(out var min, out var max);
					_boundsMin.Add(min);
					_boundsMax.Add(max);
				}
			}

			var effectiveRadius = radius;
			if (Data.UseScale)
				effectiveRadius *= point.Scale;

			var sqrRadius = effectiveRadius * effectiveRadius;
			var p = new float2(point.Position.x, point.Position.z);

			for (int i = 0; i < regions.Regions.Count; i++)
			{
				var min = _boundsMin[i];
				var max = _boundsMax[i];
				if (p.x < min.x - effectiveRadius || p.x > max.x + effectiveRadius)
					continue;
				if (p.y < min.y - effectiveRadius || p.y > max.y + effectiveRadius)
					continue;

				var polygon = regions.Regions[i];
				if (polygon.Contains(p))
					return true;

				if (polygon.DistanceToBoundarySq(p) <= sqrRadius)
					return true;
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
