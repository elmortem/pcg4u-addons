using System;
using System.Collections.Generic;
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
		public PcgOutput<PcgPointCloud> Results;
		public PcgOutput<PcgPointCloud> NearPoints;

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
					foreach (PcgPointCloud cloud in pointsList)
					{
						if (cloud == null)
							continue;

						var batchPointsCount = 1000000;
						var batchCount = cloud.Count / batchPointsCount;

						if (Results.Value == null)
							Results.Rent(cloud.Count);

						Results.Value.Append(cloud);

						for (int i = 0; i < batchCount; i++)
						{
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

		private async UniTask Process(PcgPointCloud[] pointsList, PcgPointCloud[] otherPointsList, float diameter, CancellationToken ct)
		{
			await UniTask.SwitchToThreadPool();

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

				if (ct.IsCancellationRequested)
					return;
			}

			List<int> candidateIndices;

			if (Data.RemoveThemselves)
			{
				var batchCount = math.min(16, math.max(1, totalCount / 100000));
				var batchPointsCount = flatPoints.Count / batchCount + 1;

				var resultsList = new List<int>[batchCount];
				var nearPointsList = new List<int>[batchCount];
				var tasks = new List<UniTask>(batchCount);

				for (int i = 0; i < batchCount; i++)
				{
					var pointStart = i * batchPointsCount;
					var pointCount = math.min(batchPointsCount, flatPoints.Count - i * batchPointsCount);

					resultsList[i] = new List<int>(pointCount);
					nearPointsList[i] = new List<int>(pointCount / 10 + 10);
					tasks.Add(ProcessOnce(flatPoints, pointStart, pointCount, diameter, resultsList[i],
						nearPointsList[i], ct));

					if (ct.IsCancellationRequested)
						return;
				}

				await UniTask.WhenAll(tasks);
				tasks.Clear();

				if (ct.IsCancellationRequested)
					return;

				foreach (var nearIndices in nearPointsList)
				{
					foreach (var idx in nearIndices)
						NearPoints.Value.AppendFrom(flatClouds[idx], flatIndices[idx]);
				}

				candidateIndices = new List<int>(flatPoints.Count);
				foreach (var indices in resultsList)
					candidateIndices.AddRange(indices);
			}
			else
			{
				candidateIndices = new List<int>(flatPoints.Count);
				for (int i = 0; i < flatPoints.Count; i++)
					candidateIndices.Add(i);
			}

			var finalPointsCount = candidateIndices.Count;
			var otherPointsCount = otherPointsList.TotalCount();
			var pointsBySide = math.sqrt(finalPointsCount + otherPointsCount);
			if (pointsBySide <= 0)
				return;

			var nodeSize = math.max(0.5f, Data.WorldSize / pointsBySide * 2.5f);
			var octree = new PointOctree<int>(Data.WorldSize, Data.WorldCenter, nodeSize);

			if (otherPointsCount > 0)
			{
				foreach (PcgPointCloud otherPoints in otherPointsList)
				{
					if (ct.IsCancellationRequested)
						return;

					if (otherPoints == null || otherPoints.Count <= 0)
						continue;

					for (int i = 0; i < otherPoints.Count; i++)
					{
						var point = otherPoints[i];
						octree.Add(i, point.Position);
					}
				}
			}

			ct.ThrowIfCancellationRequested();

			{
				var batchCount = math.min(16, math.max(1, finalPointsCount / 5000));
				var batchPointsCount = candidateIndices.Count / batchCount + 1;

				var resultsList = new List<int>[batchCount];
				var nearPointsList = new List<int>[batchCount];
				var tasks = new List<UniTask>(batchCount);

				for (int i = 0; i < batchCount; i++)
				{
					if (ct.IsCancellationRequested)
						return;

					var pointStart = i * batchPointsCount;
					var pointCount = math.min(batchPointsCount, candidateIndices.Count - i * batchPointsCount);

					resultsList[i] = new List<int>(pointCount);
					nearPointsList[i] = new List<int>(pointCount / 10 + 10);
					tasks.Add(FinalProcess(flatPoints, candidateIndices, pointStart, pointCount, octree, diameter, resultsList[i],
						nearPointsList[i], ct));
				}

				await UniTask.WhenAll(tasks);
				tasks.Clear();

				foreach (var indices in resultsList)
				{
					foreach (var idx in indices)
						Results.Value.AppendFrom(flatClouds[idx], flatIndices[idx]);
				}

				foreach (var indices in nearPointsList)
				{
					foreach (var idx in indices)
						NearPoints.Value.AppendFrom(flatClouds[idx], flatIndices[idx]);
				}
			}
		}

		private async UniTask FinalProcess(List<PointData> flatPoints, List<int> candidateIndices, int start, int count, PointOctree<int> octree, float diameter, List<int> results, List<int> nearPoints, CancellationToken ct)
		{
			await UniTask.SwitchToThreadPool();

			for (int j = start; j < start + count; ++j)
			{
				if (ct.IsCancellationRequested)
					return;

				var globalIndex = candidateIndices[j];
				var point = flatPoints[globalIndex];

				if (IsIntersectsLite(point, octree, diameter))
				{
					nearPoints.Add(globalIndex);

					if (Data.RemoveThemselves)
					{
						await UniTask.SwitchToMainThread();
						octree.Add(globalIndex, point.Position);
						await UniTask.SwitchToThreadPool();
					}
				}
				else
					results.Add(globalIndex);
			}
		}

		private async UniTask ProcessOnce(List<PointData> points, int start, int count, float diameter, List<int> results, List<int> nearPoints, CancellationToken ct)
		{
			await UniTask.SwitchToThreadPool();

			var pointsBySide = Mathf.Sqrt(points.Count);
			var nodeSize = math.min(Data.WorldSize, math.max(0.5f, Data.WorldSize / pointsBySide * 2.5f));
			var octree = new PointOctree<int>(Data.WorldSize, Data.WorldCenter, nodeSize);

			for (int i = start; i < start + count; ++i)
			{
				var point = points[i];

				try
				{
					if (IsIntersects(i, point, octree, diameter))
						nearPoints.Add(i);
					else
						results.Add(i);
				}
				catch (Exception e)
				{
					Debug.LogError(e);
				}

				if (ct.IsCancellationRequested)
					return;
			}
		}

		private bool IsIntersects(int index, PointData point, PointOctree<int> octree, float diameter)
		{
			if (Data.UseScale)
				diameter *= point.Scale;

			if (octree.IsColliding(point.Position, diameter))
				return true;

			if (Data.RemoveThemselves)
			{
				octree.Add(index, point.Position);
			}

			return false;
		}

		private bool IsIntersectsLite(PointData point, PointOctree<int> octree, float diameter)
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
