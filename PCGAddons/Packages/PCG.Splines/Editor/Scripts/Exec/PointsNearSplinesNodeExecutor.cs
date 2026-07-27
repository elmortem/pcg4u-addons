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
		public PcgOutput<PcgPointCloud> Results;
		public PcgOutput<PcgPointCloud> NearPoints;

		public override bool IsEmpty => Results.Value == null || NearPoints.Value == null;
		public int PointsCount => ShowResults ? Results.Value?.Count ?? 0 : NearPoints.Value?.Count ?? 0;
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var distance = GetInputValue(nameof(Data.Distance), Data.Distance);
			if (distance < 0.0001f)
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

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
			{
				Results.Rent(0);
				NearPoints.Rent(0);
				return;
			}

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

			var samples = new List<float3>();
			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in splinesList)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline == null || spline.Count < 2)
							continue;

						var splineLen = spline.GetLength();
						var count = math.clamp(Mathf.RoundToInt(splineLen / distance * 1.5f) + 2, 2, 16384);
						var step = 1f / count;
						for (int i = 0; i <= count; i++)
						{
							samples.Add(spline.EvaluatePosition(i * step));
							await scope.Step(ct: ct);
						}
					}
				}
			}

			var useScale = Data.UseScale;
			var mode = Data.Mode;
			var nearMask = new bool[flatPoints.Count];
			await PcgWorkerScheduler.RunIndexedAsync(flatPoints.Count, index =>
			{
				ct.ThrowIfCancellationRequested();
				var point = flatPoints[index];
				var effectiveDistance = useScale ? distance * point.Scale : distance;
				var sqrDist = effectiveDistance * effectiveDistance;
				var pointPosition = (float3)point.Position;
				for (int i = 0; i < samples.Count; i++)
				{
					var delta = samples[i] - pointPosition;
					if (mode == PointsNearSplinesMode.TwoD)
						delta.y = 0f;
					if (math.lengthsq(delta) < sqrDist)
					{
						nearMask[index] = true;
						return;
					}
				}
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
