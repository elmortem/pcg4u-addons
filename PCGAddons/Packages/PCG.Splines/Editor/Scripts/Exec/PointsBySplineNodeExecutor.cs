using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Points;
using PCG.Splines;
using PCG.Splines.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Exec;
using PCG.GraphModel;

namespace PCG.SelectPoints
{
	public class PointsBySplineNodeExecutor : PcgAsyncPreviewNodeExecutor<PointsBySplineNode>, IPointsCount, IShowResults
	{
		public PcgOutput<PcgPointCloud> Results;
		public PcgOutput<PcgPointCloud> Outsides;

		public override bool IsEmpty => Results.Value == null || Outsides.Value == null;
		public int PointsCount => ShowResults ? Results.Value?.Count??0 : Outsides.Value?.Count??0;
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
			{
				Results.Rent(0);
				Outsides.Rent(0);
				return;
			}

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
			{
				Results.Rent(0);
				Outsides.Rent(0);
				return;
			}

			var polygonsList = await BakePolygonsAsync(splinesList, 16, ct);

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
				Outsides.Rent(0);
				return;
			}

			var inside = new bool[flatPoints.Count];
			await PcgWorkerScheduler.RunIndexedAsync(flatPoints.Count, index =>
			{
				ct.ThrowIfCancellationRequested();
				inside[index] = CheckIntoPolygons(flatPoints[index], polygonsList);
			}, ct);

			int insideCount = 0;
			for (int i = 0; i < inside.Length; i++)
			{
				if (inside[i])
					insideCount++;
			}

			Results.Rent(insideCount);
			Outsides.Rent(flatPoints.Count - insideCount);
			for (int i = 0; i < flatPoints.Count; i++)
			{
				if (inside[i])
					Results.Value.AppendFrom(flatClouds[i], flatIndices[i]);
				else
					Outsides.Value.AppendFrom(flatClouds[i], flatIndices[i]);
			}
		}

		private async UniTask<List<List<Vector2>>[]> BakePolygonsAsync(
			PcgSplineSet[] splinesList, int resolution, CancellationToken ct)
		{
			var result = new List<List<Vector2>>[splinesList.Length];
			using var scope = OperationScope.Start(this);
			for (int i = 0; i < splinesList.Length; i++)
			{
				var splines = splinesList[i];
				var polys = new List<List<Vector2>>();
				if (splines != null)
				{
					for (int s = 0; s < splines.Count; s++)
					{
						var spline = splines[s];
						if (spline == null)
							continue;
						if (!spline.Closed)
							continue;
						Vector3[] positions = null;
						SplinesCache.GetCachedPositions(spline, resolution, out positions);
						if (positions == null)
						{
							var pts = new List<Vector2>(resolution);
							for (int k = 0; k < resolution; k++)
							{
								float t = (float)k / resolution;
								var p = spline.EvaluatePosition(t);
								pts.Add(new Vector2(p.x, p.z));
								await scope.Step(ct: ct);
							}
							polys.Add(pts);
						}
						else
						{
							var pts = new List<Vector2>(positions.Length);
							for (int k = 0; k < positions.Length; k++)
							{
								var p = positions[k];
								pts.Add(new Vector2(p.x, p.z));
								await scope.Step(ct: ct);
							}
							polys.Add(pts);
						}
					}
				}
				result[i] = polys;
			}
			return result;
		}

		private static bool PointInPolygon(UnityEngine.Vector2 p, List<UnityEngine.Vector2> poly)
		{
			bool inside = false;
			int count = poly.Count;
			for (int i = 0, j = count - 1; i < count; j = i++)
			{
				var pi = poly[i];
				var pj = poly[j];
				bool intersect = ((pi.y > p.y) != (pj.y > p.y));
				if (intersect)
				{
					float x = pj.x + (p.y - pj.y) * (pi.x - pj.x) / (pi.y - pj.y);
					if (x > p.x)
						inside = !inside;
				}
			}
			return inside;
		}

		private static bool CheckIntoPolygons(PointData point, List<List<UnityEngine.Vector2>>[] polygonsList)
		{
			var p = point.Position;
			var p2 = new UnityEngine.Vector2(p.x, p.z);
			for (int i = 0; i < polygonsList.Length; i++)
			{
				var polys = polygonsList[i];
				if (polys == null)
					continue;
				for (int k = 0; k < polys.Count; k++)
				{
					var poly = polys[k];
					if (poly == null || poly.Count <= 2)
						continue;
					if (PointInPolygon(p2, poly))
						return true;
				}
			}
			return false;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			if (ShowResults)
				GizmosUtility.DrawPoints(this, Results.Value, gizmosOptions, transform);
			else
				GizmosUtility.DrawPoints(this, Outsides.Value, gizmosOptions, transform);
		}
	}
}
