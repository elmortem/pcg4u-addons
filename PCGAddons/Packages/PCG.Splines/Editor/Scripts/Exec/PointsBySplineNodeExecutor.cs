using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Points;
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
		public PcgOutput<List<PointData>> Results;
		public PcgOutput<List<PointData>> Outsides;

		private readonly object _sync = new ();

		public override bool IsEmpty => Results.Value == null || Outsides.Value == null;
		public int PointsCount => ShowResults ? Results.Value?.Count??0 : Outsides.Value?.Count??0;
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var polygonsList = BakePolygons(splinesList, 16);

			int totalCount = pointsList.TotalCount();
			Results.Rent(totalCount);
			Outsides.Rent(totalCount);
			var batchSize = PCG.MaxGeneratePoints;
			var batches = math.max(4, (int)math.ceil((float)totalCount / batchSize));
			batchSize = (int)math.ceil((float)totalCount / batches);
			var tasks = new List<UniTask>(batches);

			foreach (var points in pointsList)
			{
				if (points == null || points.Count <= 0)
					continue;

				int remainingPoints = points.Count;
				int batchStart = 0;

				while (remainingPoints > 0)
				{
					int currentBatchSize = math.min(batchSize, remainingPoints);
					int batchEnd = batchStart + currentBatchSize;

					var task = ProcessBatch(points, batchStart, batchEnd, polygonsList, ct);
					tasks.Add(task);

					batchStart = batchEnd;
					remainingPoints -= currentBatchSize;
				}
			}

			await UniTask.WhenAll(tasks);

			await UniTaskEditor.SwitchToEditorThread();
		}

		private static List<List<Vector2>>[] BakePolygons(List<Spline>[] splinesList, int resolution)
		{
			var result = new List<List<Vector2>>[splinesList.Length];
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

		private async UniTask ProcessBatch(List<PointData> points, int start, int end, List<List<UnityEngine.Vector2>>[] polygonsList, CancellationToken ct)
		{
			await UniTask.SwitchToThreadPool();

			var batchResults = new List<PointData>(end - start);
			var batchOutsides = new List<PointData>(end - start);

			for (int i = start; i < end; i++)
			{
				ct.ThrowIfCancellationRequested();
				var point = points[i];
				if (CheckIntoPolygons(point, polygonsList))
					batchResults.Add(point);
				else
					batchOutsides.Add(point);
			}

			lock (_sync)
			{
				if (Results.Value != null && batchResults.Count > 0)
					Results.Value.AddRange(batchResults);
				if (Outsides.Value != null && batchOutsides.Count > 0)
					Outsides.Value.AddRange(batchOutsides);
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			if (ShowResults)
				GizmosUtility.DrawPoints(Results.Value, gizmosOptions, transform);
			else
				GizmosUtility.DrawPoints(Outsides.Value, gizmosOptions, transform);
		}
	}
}
