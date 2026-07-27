using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.TransformPoints
{
	public class DensityByDistanceToSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<DensityByDistanceToSplinesNode>, IPointsCount
	{
		public PcgOutput<PcgPointCloud> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
			{
				Results.Rent(0);
				return;
			}

			var radius = GetInputValue(nameof(Data.Radius), Data.Radius);

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
				return;
			}

			if (radius < 0.0001f)
			{
				Results.Rent(flatPoints.Count);
				for (int i = 0; i < flatPoints.Count; i++)
					Results.Value.AppendFrom(flatClouds[i], flatIndices[i]);
				return;
			}

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			var mode = Data.Mode;
			const int curveResolution = 256;
			var curveLut = new float[curveResolution + 1];
			for (int i = 0; i <= curveResolution; i++)
				curveLut[i] = Data.Curve.Evaluate(i / (float)curveResolution);

			var samples = new List<float3>();
			if (splinesList != null)
			{
				var sampleStep = radius * 0.25f;
				using var scope = OperationScope.Start(this);
				foreach (var splines in splinesList)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline.Count <= 1)
							continue;

						var length = spline.GetLength();
						var count = math.clamp((int)math.round(length / sampleStep), 2, 4096);
						for (int i = 0; i <= count; i++)
						{
							samples.Add(spline.EvaluatePosition(i / (float)count));
							await scope.Step(ct: ct);
						}
					}
				}
			}

			var computed = await PcgWorkerScheduler.RunAsync(() =>
			{
				var cells = new Dictionary<int3, List<int>>();
				for (int i = 0; i < samples.Count; i++)
				{
					var cell = (int3)math.floor(samples[i] / radius);
					if (!cells.TryGetValue(cell, out var list))
					{
						list = new List<int>();
						cells.Add(cell, list);
					}
					list.Add(i);
				}

				var output = new List<PointData>(flatPoints);
				for (int i = 0; i < output.Count; i++)
				{
					ct.ThrowIfCancellationRequested();

					var point = output[i];
					var minDistSq = radius * radius;

					if (samples.Count > 0)
					{
						var center = (int3)math.floor(point.Position / radius);
						for (int x = -1; x <= 1; x++)
						{
							for (int y = -1; y <= 1; y++)
							{
								for (int z = -1; z <= 1; z++)
								{
									if (!cells.TryGetValue(center + new int3(x, y, z), out var list))
										continue;

									for (int t = 0; t < list.Count; t++)
									{
										var distSq = math.distancesq(point.Position, samples[list[t]]);
										if (distSq < minDistSq)
											minDistSq = distSq;
									}
								}
							}
						}
					}

					var t01 = math.clamp(math.sqrt(minDistSq) / radius, 0f, 1f);
					var scaled = t01 * curveResolution;
					var curveIndex = math.min((int)scaled, curveResolution - 1);
					var value = math.lerp(curveLut[curveIndex], curveLut[curveIndex + 1], scaled - curveIndex);

					if (mode == ChangeDensityMode.Set)
						point.Density = value;
					else if (mode == ChangeDensityMode.Add)
						point.Density += value;
					else
						point.Density *= value;

					point.Density = math.clamp(point.Density, 0f, 1f);
					output[i] = point;
				}
				return output;
			}, ct);

			Results.Rent(flatPoints.Count);
			for (int i = 0; i < flatPoints.Count; i++)
				Results.Value.AppendFrom(flatClouds[i], flatIndices[i], computed[i]);
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			GizmosUtility.DrawPointsByDensity(this, Results.Value, gizmosOptions, transform);
		}
	}
}
