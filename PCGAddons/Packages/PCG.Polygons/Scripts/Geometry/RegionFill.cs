using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public static class RegionFill
	{
		public static async UniTask FillRandom(OperationScope scope, List<PointData> results, IList<Polygon2D> polygons, float planeY, int count, int seed, CancellationToken ct = default)
		{
			if (count <= 0 || polygons.Count == 0)
				return;

			count = math.min(count, PCG.MaxListPoints);
			GetBounds(polygons, out var min, out var max);
			var random = PcgRandom.Create(seed);

			int added = 0;
			int tryCount = count * 8;
			while (added < count && tryCount-- > 0)
			{
				var sample = new float2(random.NextFloat(min.x, max.x), random.NextFloat(min.y, max.y));
				if (ContainsAny(polygons, sample))
				{
					results.Add(new PointData
					{
						Position = new float3(sample.x, planeY, sample.y),
						Normal = new float3(0f, 1f, 0f),
						Scale = 1f
					});
					added++;
				}

				await scope.Step(ct: ct);
			}
		}

		public static async UniTask FillGrid(OperationScope scope, List<PointData> results, IList<Polygon2D> polygons, float planeY, float spacing, CancellationToken ct = default)
		{
			if (spacing <= 0f || polygons.Count == 0)
				return;

			GetBounds(polygons, out var min, out var max);

			for (float x = min.x; x <= max.x; x += spacing)
			{
				for (float y = min.y; y <= max.y; y += spacing)
				{
					var sample = new float2(x, y);
					if (ContainsAny(polygons, sample))
					{
						results.Add(new PointData
						{
							Position = new float3(sample.x, planeY, sample.y),
							Normal = new float3(0f, 1f, 0f),
							Scale = 1f
						});
					}

					await scope.Step(ct: ct);
				}
			}
		}

		public static bool ContainsAny(IList<Polygon2D> polygons, float2 p)
		{
			for (int i = 0; i < polygons.Count; i++)
			{
				if (polygons[i].Contains(p))
					return true;
			}

			return false;
		}

		private static void GetBounds(IList<Polygon2D> polygons, out float2 min, out float2 max)
		{
			min = new float2(float.MaxValue, float.MaxValue);
			max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < polygons.Count; i++)
			{
				polygons[i].GetBounds(out var pmin, out var pmax);
				min = math.min(min, pmin);
				max = math.max(max, pmax);
			}
		}
	}
}
