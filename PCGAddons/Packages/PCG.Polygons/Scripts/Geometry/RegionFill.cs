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
		public static async UniTask FillRandom(OperationScope scope, List<PointData> results, Polygon2D polygon, float planeY, int count, int seed, CancellationToken ct = default)
		{
			if (count <= 0)
				return;

			count = math.min(count, PCG.MaxListPoints);
			polygon.GetBounds(out var min, out var max);
			var random = PcgRandom.Create(seed);

			int tryCount = count * 4;
			while (results.Count < count && tryCount-- > 0)
			{
				var sample = new float2(random.NextFloat(min.x, max.x), random.NextFloat(min.y, max.y));
				if (polygon.Contains(sample))
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

		public static async UniTask FillGrid(OperationScope scope, List<PointData> results, Polygon2D polygon, float planeY, float spacing, CancellationToken ct = default)
		{
			if (spacing <= 0f)
				return;

			polygon.GetBounds(out var min, out var max);

			for (float x = min.x; x <= max.x; x += spacing)
			{
				for (float y = min.y; y <= max.y; y += spacing)
				{
					var sample = new float2(x, y);
					if (polygon.Contains(sample))
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
	}
}
