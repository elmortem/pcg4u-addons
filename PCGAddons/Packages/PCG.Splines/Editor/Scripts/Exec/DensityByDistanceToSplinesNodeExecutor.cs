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
		public PcgOutput<List<PointData>> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var radius = GetInputValue(nameof(Data.Radius), Data.Radius);

			var results = Results.Rent(pointsList.TotalCount());
			foreach (var points in pointsList)
			{
				if (points == null || points.Count <= 0)
					continue;
				results.AddRange(points);
			}

			if (radius < 0.0001f)
				return;

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);

			var samples = new List<float3>();
			if (splinesList != null)
			{
				var sampleStep = radius * 0.25f;
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
							samples.Add(spline.EvaluatePosition(i / (float)count));
					}
				}
			}

			await UniTask.SwitchToThreadPool();

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

			for (int i = 0; i < results.Count; i++)
			{
				ct.ThrowIfCancellationRequested();

				var point = results[i];
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
				var value = Data.Curve.Evaluate(t01);

				if (Data.Mode == ChangeDensityMode.Set)
					point.Density = value;
				else if (Data.Mode == ChangeDensityMode.Add)
					point.Density += value;
				else
					point.Density *= value;

				point.Density = math.clamp(point.Density, 0f, 1f);
				results[i] = point;
			}

			await UniTaskEditor.SwitchToEditorThread();
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			GizmosUtility.DrawPointsByDensity(this, Results.Value, gizmosOptions, transform);
		}
	}
}
