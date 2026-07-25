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
		public PcgOutput<List<PointData>> Results;
		public PcgOutput<List<PointData>> NearPoints;

		public override bool IsEmpty => Results.Value == null || NearPoints.Value == null;
		public int PointsCount => ShowResults ? Results.Value?.Count ?? 0 : NearPoints.Value?.Count ?? 0;
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var distance = GetInputValue(nameof(Data.Distance), Data.Distance);
			if (distance < 0.0001f)
				return;

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return;

			int totalCount = pointsList.TotalCount();
			var pointsSnapshot = new List<PointData>(totalCount);
			foreach (var points in pointsList)
			{
				if (points != null)
					pointsSnapshot.AddRange(points);
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
			var nearMask = new bool[pointsSnapshot.Count];
			await PcgWorkerScheduler.RunIndexedAsync(pointsSnapshot.Count, index =>
			{
				ct.ThrowIfCancellationRequested();
				var point = pointsSnapshot[index];
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
