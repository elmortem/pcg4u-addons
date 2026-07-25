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

			int totalCount = pointsList.TotalCount();
			var pointsSnapshot = new List<PointData>(totalCount);
			foreach (var points in pointsList)
			{
				if (points != null)
					pointsSnapshot.AddRange(points);
			}

			var boundsMin = new float2[hasRegions ? regions.Regions.Count : 0];
			var boundsMax = new float2[boundsMin.Length];
			for (int i = 0; i < boundsMin.Length; i++)
				regions.Regions[i].GetBounds(out boundsMin[i], out boundsMax[i]);

			var useScale = Data.UseScale;
			var nearMask = new bool[pointsSnapshot.Count];
			await PcgWorkerScheduler.RunIndexedAsync(pointsSnapshot.Count, index =>
			{
				ct.ThrowIfCancellationRequested();
				if (hasRegions && CheckNearRegion(pointsSnapshot[index], regions, boundsMin, boundsMax, radius, useScale))
					nearMask[index] = true;
			}, ct);

			var results = new List<PointData>(totalCount);
			var nearPoints = new List<PointData>(totalCount / 10 + 10);
			for (int i = 0; i < pointsSnapshot.Count; i++)
			{
				if (nearMask[i])
					nearPoints.Add(pointsSnapshot[i]);
				else
					results.Add(pointsSnapshot[i]);
			}

			Results.Value = results;
			NearPoints.Value = nearPoints;
		}

		private static bool CheckNearRegion(
			PointData point,
			RegionSet regions,
			float2[] boundsMin,
			float2[] boundsMax,
			float radius,
			bool useScale)
		{
			var effectiveRadius = radius;
			if (useScale)
				effectiveRadius *= point.Scale;

			var sqrRadius = effectiveRadius * effectiveRadius;
			var p = new float2(point.Position.x, point.Position.z);

			for (int i = 0; i < regions.Regions.Count; i++)
			{
				var min = boundsMin[i];
				var max = boundsMax[i];
				if (p.x < min.x - effectiveRadius || p.x > max.x + effectiveRadius)
					continue;
				if (p.y < min.y - effectiveRadius || p.y > max.y + effectiveRadius)
					continue;

				var polygon = regions.Regions[i];
				if (polygon.Contains(p) || polygon.DistanceToBoundarySq(p) <= sqrRadius)
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
