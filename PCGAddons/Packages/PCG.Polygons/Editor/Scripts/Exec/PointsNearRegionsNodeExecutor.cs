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
		public PcgOutput<PcgPointCloud> Results;
		public PcgOutput<PcgPointCloud> NearPoints;

		public override bool IsEmpty => Results.Value == null || NearPoints.Value == null;
		public int PointsCount => ShowResults ? Results.Value?.Count ?? 0 : NearPoints.Value?.Count ?? 0;
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var radius = GetInputValue(nameof(Data.Radius), Data.Radius);
			if (radius < 0.0001f)
			{
				Results.Rent(0);
				NearPoints.Rent(0);
				return;
			}

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
			{
				Results.Rent(0);
				NearPoints.Rent(0);
				return;
			}

			var regions = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Regions), ct);
			var hasRegions = regions != null && regions.Count > 0;

			var totalCount = pointsList.TotalCount();
			var flatPoints = new List<PointData>(totalCount);
			var flatClouds = new List<PcgPointCloud>(totalCount);
			var flatIndices = new List<int>(totalCount);
			foreach (PcgPointCloud cloud in pointsList)
			{
				if (cloud == null || cloud.Count == 0)
					continue;

				for (int idx = 0; idx < cloud.Count; idx++)
				{
					flatPoints.Add(cloud[idx]);
					flatClouds.Add(cloud);
					flatIndices.Add(idx);
				}
			}

			if (flatPoints.Count == 0)
			{
				Results.Rent(0);
				NearPoints.Rent(0);
				return;
			}

			var boundsMin = new float2[hasRegions ? regions.Regions.Count : 0];
			var boundsMax = new float2[boundsMin.Length];
			for (int i = 0; i < boundsMin.Length; i++)
				regions.Regions[i].GetBounds(out boundsMin[i], out boundsMax[i]);

			var useScale = Data.UseScale;
			var nearMask = new bool[flatPoints.Count];
			await PcgWorkerScheduler.RunIndexedAsync(flatPoints.Count, index =>
			{
				ct.ThrowIfCancellationRequested();
				if (hasRegions && CheckNearRegion(flatPoints[index], regions, boundsMin, boundsMax, radius, useScale))
					nearMask[index] = true;
			}, ct);

			int nearCount = 0;
			for (int i = 0; i < nearMask.Length; i++)
			{
				if (nearMask[i])
					nearCount++;
			}

			Results.Rent(flatPoints.Count - nearCount);
			NearPoints.Rent(nearCount);
			for (int i = 0; i < flatPoints.Count; i++)
			{
				if (nearMask[i])
					NearPoints.Value.AppendFrom(flatClouds[i], flatIndices[i]);
				else
					Results.Value.AppendFrom(flatClouds[i], flatIndices[i]);
			}
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
				GizmosUtility.DrawPoints(this, Results.Value, gizmosOptions, transform);
			else
				GizmosUtility.DrawPoints(this, NearPoints.Value, gizmosOptions, transform);
		}
	}
}
