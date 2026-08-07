using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.SelectPoints
{
	public class PointsByWaterSurfaceNodeExecutor : PcgAsyncPreviewNodeExecutor<PointsByWaterSurfaceNode>, IPointsCount, IShowResults
	{
		public PcgOutput<PcgPointCloud> AboveWater;
		public PcgOutput<PcgPointCloud> BelowWater;

		public override bool IsEmpty => AboveWater.Value == null || BelowWater.Value == null;
		public int PointsCount => ShowResults ? (AboveWater.Value?.Count ?? 0) : (BelowWater.Value?.Count ?? 0);
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length == 0)
			{
				AboveWater.Rent(0);
				BelowWater.Rent(0);
				return;
			}

			var flatPoints = new List<PointData>(pointsList.TotalCount());
			var flatClouds = new List<PcgPointCloud>(pointsList.TotalCount());
			var flatIndices = new List<int>(pointsList.TotalCount());

			using var scope = OperationScope.Start(this);

			foreach (PcgPointCloud cloud in pointsList)
			{
				if (cloud == null || cloud.Count == 0)
					continue;

				for (int idx = 0; idx < cloud.Count; idx++)
				{
					flatPoints.Add(cloud[idx]);
					flatClouds.Add(cloud);
					flatIndices.Add(idx);
					if (scope.ShouldYield(ct: ct))
						await scope.YieldAsync(ct);
				}
			}

			if (flatPoints.Count == 0)
			{
				AboveWater.Rent(0);
				BelowWater.Rent(0);
				return;
			}

			var waterSurface = GetInputValue(nameof(Data.WaterSurface), Data.WaterSurface);
			if (waterSurface == null)
			{
				AboveWater.Rent(0);
				BelowWater.Rent(0);
				return;
			}

			var offset = GetInputValue(nameof(Data.Offset), Data.Offset);
			var waterLevel = waterSurface.transform.position.y + offset;
			var hostTransform = Graph.GetHostTransform();
			var localToWorld = hostTransform != null
				? (float4x4)hostTransform.localToWorldMatrix
				: float4x4.identity;

			var total = flatPoints.Count;
			var batchSize = PCG.MaxGeneratePoints;
			var batchCount = math.max(4, (int)math.ceil((float)total / batchSize));
			batchSize = (int)math.ceil((float)total / batchCount);

			var aboveBatches = new List<int>[batchCount];
			var belowBatches = new List<int>[batchCount];
			var tasks = new List<UniTask>(batchCount);

			int batchIndex = 0;
			int start = 0;
			while (start < total)
			{
				int end = math.min(start + batchSize, total);
				tasks.Add(ProcessBatch(flatPoints, start, end, waterLevel, localToWorld,
					aboveBatches, belowBatches, batchIndex, ct));
				start = end;
				batchIndex++;
			}

			await UniTask.WhenAll(tasks);
			await UniTaskEditor.SwitchToEditorThread();

			int aboveCount = 0;
			int belowCount = 0;
			for (int i = 0; i < batchCount; i++)
			{
				aboveCount += aboveBatches[i]?.Count ?? 0;
				belowCount += belowBatches[i]?.Count ?? 0;
			}

			AboveWater.Rent(aboveCount);
			BelowWater.Rent(belowCount);
			for (int i = 0; i < batchCount; i++)
			{
				if (aboveBatches[i] != null)
				{
					foreach (var idx in aboveBatches[i])
						AboveWater.Value.AppendFrom(flatClouds[idx], flatIndices[idx]);

					await scope.Step(aboveBatches[i].Count, 1, ct);
				}

				if (belowBatches[i] != null)
				{
					foreach (var idx in belowBatches[i])
						BelowWater.Value.AppendFrom(flatClouds[idx], flatIndices[idx]);

					await scope.Step(belowBatches[i].Count, 1, ct);
				}
			}
		}

		private async UniTask ProcessBatch(List<PointData> points, int start, int end, float waterLevel,
			float4x4 localToWorld, List<int>[] aboveBatches, List<int>[] belowBatches,
			int batchIndex, CancellationToken ct)
		{
			await UniTask.SwitchToThreadPool();

			var aboveWater = new List<int>();
			var belowWater = new List<int>();

			for (int i = start; i < end; i++)
			{
				if (((i - start) & 1023) == 0)
				{
					ct.ThrowIfCancellationRequested();
					PcgComputeSystem.ReportProgress(this);
				}

				var point = points[i];
				var worldPosition = math.transform(localToWorld, point.Position);
				if (worldPosition.y >= waterLevel)
					aboveWater.Add(i);
				else
					belowWater.Add(i);
			}

			aboveBatches[batchIndex] = aboveWater;
			belowBatches[batchIndex] = belowWater;
		}

		public override int GetVersionSalt()
		{
			unchecked
			{
				if (Data.WaterSurface == null)
					return 0;

				int hash = Data.WaterSurface.transform.position.y.GetHashCode();
				var hostTransform = Graph.GetHostTransform();
				if (hostTransform != null)
					hash = (hash * 397) ^ hostTransform.localToWorldMatrix.GetHashCode();

				return hash;
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var points = ShowResults ? AboveWater.Value : BelowWater.Value;
			GizmosUtility.DrawPoints(this, points, GetGizmosOptions(), transform);
		}
	}
}
