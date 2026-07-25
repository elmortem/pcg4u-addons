using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Modes;
using PCG.Points;
using PCG.Splines.Utilities;
using PCG.Utilities;

namespace PCG.Splines.Surfaces
{
	public static class SplinePoints
	{
		public static async UniTask GetPoints(OperationScope scope, List<PointData> results, Spline spline, GeneratePointMode mode, int count, Vector3 offset, int seed, CancellationToken ct = default)
		{
			if (spline == null)
			{
				Debug.LogWarning("Spline is not assigned.");
				return;
			}

			if (count <= 0)
				return;

			switch (mode)
			{
				case GeneratePointMode.SurfaceRegular:
					await GetSurfaceRegularPoints(scope, results, spline, count, offset, ct);
					break;
				case GeneratePointMode.VolumeRegular:
					await GetVolumeRegularPoints(scope, results, spline, count, offset, ct);
					break;
				case GeneratePointMode.SurfaceRandom:
					await GetSurfaceRandomPoints(scope, results, spline, count, offset, seed, ct);
					break;
				case GeneratePointMode.VolumeRandom:
					await GetVolumeRandomPoints(scope, results, spline, count, offset, seed, ct);
					break;
			}
		}

		public static async UniTask GetPointsByDistance(OperationScope scope, List<PointData> results, Spline spline, float distance, bool distribute, CancellationToken ct = default)
		{
			if (spline == null)
			{
				Debug.LogWarning("Spline is not assigned.");
				return;
			}

			if (distance <= 0f)
				return;

			var length = spline.GetLength();
			if (length <= 0f)
				return;

			var intervals = math.max(1, Mathf.RoundToInt(length / distance));

			int count;
			float step;

			if (distribute)
			{
				step = length / intervals;
				count = spline.Closed ? intervals : intervals + 1;
			}
			else
			{
				step = distance;
				count = Mathf.FloorToInt(length / distance) + 1;
				if (spline.Closed && Mathf.Approximately((count - 1) * distance, length))
					count -= 1;
			}

			count = math.min(count, PCG.MaxListPoints);

			for (int i = 0; i < count; i++)
			{
				var pointDistance = step * i;
				var t = spline.ConvertIndexUnit(pointDistance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
				spline.Evaluate(t, out var point, out var tangent, out var upVector);
				results.Add(new PointData
				{
					Position = (Vector3)point,
					Normal = upVector,
					Scale = 1f,
					Angle = Quaternion.LookRotation(tangent, upVector).eulerAngles.y
				});

				await scope.Step(ct: ct);
			}
		}

		private static async UniTask GetSurfaceRegularPoints(OperationScope scope, List<PointData> results, Spline spline, int count, Vector3 offset, CancellationToken ct)
		{
			var length = spline.GetLength();
			if (length <= 0f)
				return;

			for (int i = 0; i < count; ++i)
			{
				var distance = length * i / count;
				var t = spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
				spline.Evaluate(t, out var point, out var tangent, out var upVector);
				results.Add(new PointData
				{
					Position = offset + (Vector3)point,
					Normal = upVector,
					Scale = 1f,
					Angle = Quaternion.LookRotation(tangent, upVector).eulerAngles.y
				});

				await scope.Step(ct: ct);
			}
		}

		private static async UniTask GetVolumeRegularPoints(OperationScope scope, List<PointData> results, Spline spline, int count, Vector3 offset, CancellationToken ct)
		{
			if (!spline.Closed)
				return;

			count = math.min(count, PCG.MaxListPoints);
			
			float3 splineUp = float3.zero;
			float curving = 0f;
			for (var i = 0; i < spline.Count; i++)
			{
				splineUp += spline.GetCurveUpVector(i, 0f);

				var tangentMode = spline.GetTangentMode(i);
				if(tangentMode == TangentMode.AutoSmooth || tangentMode == TangentMode.Linear)
					curving += 1f;
			}
			var broking = 1f - curving / spline.Count;
				
			var bounds = spline.GetBounds();

			var sizeCount = Mathf.RoundToInt(Mathf.Sqrt(count * (2f - broking)));

			var sizeWidth = bounds.size.x / sizeCount;
			var sizeHeight = bounds.size.z / sizeCount;
			
			for (int i = 0; i < sizeCount; i++)
			{
				for (int j = 0; j < sizeCount; j++)
				{
					var point = new Vector3(bounds.min.x + i * sizeWidth + sizeWidth * 0.5f, bounds.center.y,
						bounds.min.z + j * sizeHeight + sizeHeight * 0.5f);
					if (spline.IsInsideSpline(point))
					{
						results.Add(new PointData { Position = point, Normal = splineUp, Scale = 1f });
					}
					
					await scope.Step(ct: ct);
				}
			}
		}

		private static async UniTask GetSurfaceRandomPoints(OperationScope scope, List<PointData> results, Spline spline, int count,
			Vector3 offset, int seed, CancellationToken ct)
		{
			var random = PcgRandom.Create(seed);

			count = math.min(count, PCG.MaxListPoints);

			for (int i = 0; i < count; i++)
			{
				spline.Evaluate(random.NextFloat(), out var point, out var tangent, out var upVector);
				results.Add(new PointData
				{
					Position = offset + (Vector3)point,
					Normal = upVector,
					Scale = 1f,
					Angle = Quaternion.LookRotation(tangent, upVector).eulerAngles.y
				});

				await scope.Step(ct: ct);
			}
		}

		private static async UniTask GetVolumeRandomPoints(OperationScope scope, List<PointData> results, Spline spline, int count,
			Vector3 offset, int seed, CancellationToken ct)
		{
			if (!spline.Closed)
				return;
			
			count = math.min(count, PCG.MaxListPoints);

			var random = PcgRandom.Create(seed);

			float3 splineUp = float3.zero;
			for (var i = 0; i < spline.Count; i++)
			{
				splineUp += spline.GetCurveUpVector(i, 0f);
			}
			var bounds = spline.GetBounds();

			var tryCount = count * 3;
			while(results.Count < count && tryCount-- > 0)
			{
				var point = new Vector3(random.NextFloat(bounds.min.x, bounds.max.x), bounds.center.y,
					random.NextFloat(bounds.min.z, bounds.max.z));
				if (spline.IsInsideSpline(point))
				{
					results.Add(new PointData { Position = point + offset, Normal = splineUp, Scale = 1f });
				}

				await scope.Step(ct: ct);
			}
		}
	}
}
