using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Octree;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Octree
{
	public class PointsNearPointsOctreeNodeExecutor : PcgAsyncPreviewNodeExecutor<PointsNearPointsOctreeNode>, IPointsCount, IShowResults
	{
		public PcgOutput<List<PointData>> Results;
		public PcgOutput<List<PointData>> NearPoints;

		public override bool IsEmpty => Results.Value == null || NearPoints.Value == null;
		public int PointsCount => ShowResults ? (Results.Value?.Count ?? 0) : (NearPoints.Value?.Count ?? 0);
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var otherPointsList = GetInputValues(nameof(Data.OtherPoints), Data.OtherPoints);
			if (otherPointsList.TotalCount() <= 0 && !Data.RemoveThemselves)
			{
				using (var scope = OperationScope.Start(this))
				{
					foreach (List<PointData> points in pointsList)
					{
						if (points == null)
							continue;

						var batchPointsCount = 1000000;
						var batchCount = points.Count / batchPointsCount;

						if (Results.Value == null)
							Results.Rent(points.Count);
						for (int i = 0; i < batchCount; i++)
						{
							var pointStart = i * batchPointsCount;
							var pointCount = math.min(batchPointsCount, points.Count - i * batchPointsCount);

							Results.Value.AddRange(points.GetRange(pointStart, pointCount));

							await scope.Step(ct: ct);
						}
					}
					return;
				}
			}

			var diameter = GetInputValue(nameof(Data.Radius), Data.Radius) * 2f;

			var finalPointsCount = pointsList.TotalCount();
			Results.Rent(finalPointsCount);
			NearPoints.Rent(finalPointsCount / 10 + 10);

			await Process(pointsList, otherPointsList, diameter, ct);

			await UniTaskEditor.SwitchToEditorThread();
		}

		private async UniTask Process(List<PointData>[] pointsList, List<PointData>[] otherPointsList, float diameter, CancellationToken ct)
		{
			await UniTask.SwitchToThreadPool();

			var flatPoints = new List<PointData>();
			List<PointData>[] finalPointsList;

			if (Data.RemoveThemselves)
			{
				var totalCount = pointsList.TotalCount();
				flatPoints.Capacity = totalCount;
				foreach (List<PointData> points in pointsList)
				{
					if (points == null)
						continue;
					flatPoints.AddRange(points);

					if (ct.IsCancellationRequested)
						return;
				}

				var batchCount = math.min(16, math.max(1, totalCount / 100000));
				var batchPointsCount = flatPoints.Count / batchCount + 1;

				var resultsList = new List<PointData>[batchCount];
				var nearPointsList = new List<PointData>[batchCount];
				var tasks = new List<UniTask>(batchCount);

				for (int i = 0; i < batchCount; i++)
				{
					var pointStart = i * batchPointsCount;
					var pointCount = math.min(batchPointsCount, flatPoints.Count - i * batchPointsCount);

					resultsList[i] = new List<PointData>(pointCount);
					nearPointsList[i] = new List<PointData>(pointCount / 10 + 10);
					tasks.Add(ProcessOnce(flatPoints, pointStart, pointCount, diameter, resultsList[i],
						nearPointsList[i], ct));

					if (ct.IsCancellationRequested)
						return;
				}

				await UniTask.WhenAll(tasks);
				tasks.Clear();

				if (ct.IsCancellationRequested)
					return;

				foreach (var nearPoints in nearPointsList)
				{
					NearPoints.Value.AddRange(nearPoints);
				}

				finalPointsList = resultsList;
			}
			else
			{
				finalPointsList = pointsList;
			}

			var finalPointsCount = finalPointsList.TotalCount();
			var otherPointsCount = otherPointsList.TotalCount();
			var pointsBySide = math.sqrt(finalPointsCount + otherPointsCount);
			if (pointsBySide <= 0)
				return;

			var nodeSize = math.max(0.5f, Data.WorldSize / pointsBySide * 2.5f);
			var octree = new PointOctree<PointData>(Data.WorldSize, Data.WorldCenter, nodeSize);

			if (otherPointsCount > 0)
			{
				foreach (List<PointData> otherPoints in otherPointsList)
				{
					if (ct.IsCancellationRequested)
						return;

					if (otherPoints == null || otherPoints.Count <= 0)
						continue;

					for (int i = 0; i < otherPoints.Count; i++)
					{
						var point = otherPoints[i];
						octree.Add(point, point.Position);
					}
				}
			}

			ct.ThrowIfCancellationRequested();

			{
				flatPoints.Clear();
				flatPoints.Capacity = finalPointsCount;
				flatPoints.AddRange(finalPointsList.Where(p => p != null).SelectMany(p => p));

				var batchCount = math.min(16, math.max(1, finalPointsCount / 5000));
				var batchPointsCount = flatPoints.Count / batchCount + 1;

				var resultsList = new List<PointData>[batchCount];
				var nearPointsList = new List<PointData>[batchCount];
				var tasks = new List<UniTask>(batchCount);

				for (int i = 0; i < batchCount; i++)
				{
					if (ct.IsCancellationRequested)
						return;

					var pointStart = i * batchPointsCount;
					var pointCount = math.min(batchPointsCount, flatPoints.Count - i * batchPointsCount);

					resultsList[i] = new List<PointData>(pointCount);
					nearPointsList[i] = new List<PointData>(pointCount / 10 + 10);
					tasks.Add(FinalProcess(flatPoints, pointStart, pointCount, octree, diameter, resultsList[i],
						nearPointsList[i], ct));
				}

				await UniTask.WhenAll(tasks);
				tasks.Clear();

				Results.Value.AddRange(resultsList.SelectMany(p => p));
				NearPoints.Value.AddRange(nearPointsList.SelectMany(p => p));
			}
		}

		private async UniTask FinalProcess(List<PointData> finalPoints, int start, int count, PointOctree<PointData> octree, float diameter, List<PointData> results, List<PointData> nearPoints, CancellationToken ct)
		{
			await UniTask.SwitchToThreadPool();

			for (int j = start; j < start + count; ++j)
			{
				if (ct.IsCancellationRequested)
					return;

				var point = finalPoints[j];

				if (IsIntersectsLite(point, octree, diameter))
				{
					nearPoints.Add(point);

					if (Data.RemoveThemselves)
					{
						await UniTask.SwitchToMainThread();
						octree.Add(point, point.Position);
						await UniTask.SwitchToThreadPool();
					}
				}
				else
					results.Add(point);
			}
		}

		private async UniTask ProcessOnce(List<PointData> points, int start, int count, float diameter, List<PointData> results, List<PointData> nearPoints, CancellationToken ct)
		{
			await UniTask.SwitchToThreadPool();

			var pointsBySide = Mathf.Sqrt(points.Count);
			var nodeSize = math.min(Data.WorldSize, math.max(0.5f, Data.WorldSize / pointsBySide * 2.5f));
			var octree = new PointOctree<PointData>(Data.WorldSize, Data.WorldCenter, nodeSize);

			for (int i = start; i < start + count; ++i)
			{
				var point = points[i];

				try
				{
					if (IsIntersects(point, octree, diameter))
						nearPoints.Add(point);
					else
						results.Add(point);
				}
				catch (Exception e)
				{
					Debug.LogError(e);
				}

				if (ct.IsCancellationRequested)
					return;
			}
		}

		private bool IsIntersects(PointData point, PointOctree<PointData> octree, float diameter)
		{
			if (Data.UseScale)
				diameter *= point.Scale;

			if (octree.IsColliding(point.Position, diameter))
				return true;

			if (Data.RemoveThemselves)
			{
				octree.Add(point, point.Position);
			}

			return false;
		}

		private bool IsIntersectsLite(PointData point, PointOctree<PointData> octree, float diameter)
		{
			if (Data.UseScale)
				diameter *= point.Scale;

			if (octree.IsColliding(point.Position, diameter))
				return true;

			return false;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			var color = Gizmos.color;
			Gizmos.color = gizmosOptions.Color;
			Gizmos.DrawWireCube(Data.WorldCenter, new Vector3(Data.WorldSize, Data.WorldSize, Data.WorldSize));
			Gizmos.color = color;

			if (IsEmpty)
				return;

			if (ShowResults)
				GizmosUtility.DrawPoints(this, Results.Value, gizmosOptions, transform);
			else
				GizmosUtility.DrawPoints(this, NearPoints.Value, gizmosOptions, transform);
		}
	}
}
