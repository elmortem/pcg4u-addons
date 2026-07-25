using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using PcgRandom = PCG.Utilities.PcgRandom;

namespace PCG.Polygons.City
{
	public class SubdivideRegionNodeExecutor : PcgAsyncPreviewNodeExecutor<SubdivideRegionNode>
	{
		public PcgOutput<RegionSet> Blocks;

		public override bool IsEmpty => Blocks.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (input == null)
			{
				Blocks.Value = new RegionSet();
				return;
			}

			var minSize = GetInputValue(nameof(Data.MinSize), Data.MinSize);
			var maxDepth = GetInputValue(nameof(Data.MaxDepth), Data.MaxDepth);
			var splitJitter = GetInputValue(nameof(Data.SplitJitter), Data.SplitJitter);
			var rotation = GetInputValue(nameof(Data.Rotation), Data.Rotation);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);

			Blocks.Value = await PcgWorkerScheduler.RunAsync(
				() => Compute(input, minSize, maxDepth, splitJitter, rotation, seed, ct),
				ct);
		}

		private static RegionSet Compute(
			RegionSet input,
			float minSize,
			int maxDepth,
			float splitJitter,
			float rotation,
			int seed,
			CancellationToken ct)
		{
			var result = new RegionSet
			{
				PlaneY = input.PlaneY
			};
			var random = PcgRandom.Create(seed);
			var queue = new Queue<(Polygon2D polygon, int depth, float2 pivot)>();
			for (int i = 0; i < input.Regions.Count; i++)
			{
				var polygon = input.Regions[i].Clone();
				polygon.GetBounds(out var min, out var max);
				var pivot = (min + max) * 0.5f;
				Rotate(polygon, pivot, -rotation);
				queue.Enqueue((polygon, 0, pivot));
			}

			var left = new List<Polygon2D>();
			var right = new List<Polygon2D>();

			while (queue.Count > 0)
			{
				ct.ThrowIfCancellationRequested();
				var (polygon, depth, pivot) = queue.Dequeue();
				polygon.GetBounds(out var min, out var max);
				var size = max - min;
				float maxDim = math.max(size.x, size.y);

				if (maxDim < minSize || depth >= maxDepth)
				{
					Rotate(polygon, pivot, rotation);
					int row = result.AddRegion(polygon);
					result.Attributes.Set(CityAttributes.Depth, row, depth);
					continue;
				}

				bool splitX = size.x >= size.y;
				float t = 0.5f + random.NextFloat(-splitJitter, splitJitter);
				float2 a;
				float2 b;
				if (splitX)
				{
					float x = math.lerp(min.x, max.x, t);
					a = new float2(x, min.y - 1f);
					b = new float2(x, max.y + 1f);
				}
				else
				{
					float y = math.lerp(min.y, max.y, t);
					a = new float2(min.x - 1f, y);
					b = new float2(max.x + 1f, y);
				}

				int cutDepth = depth + 1;
				left.Clear();
				right.Clear();
				PolygonClipper.SplitByLine(polygon, a, b, left, right, (attrs, row) => attrs.Set(CityAttributes.CutDepth, row, cutDepth));

				for (int i = 0; i < left.Count; i++)
					queue.Enqueue((left[i], depth + 1, pivot));
				for (int i = 0; i < right.Count; i++)
					queue.Enqueue((right[i], depth + 1, pivot));
			}

			return result;
		}

		private static void Rotate(Polygon2D polygon, float2 pivot, float degrees)
		{
			if (math.abs(degrees) < 0.0001f)
				return;

			float radians = math.radians(degrees);
			math.sincos(radians, out float sin, out float cos);
			RotateRing(polygon.Outer, pivot, sin, cos);
			for (int i = 0; i < polygon.Holes.Count; i++)
				RotateRing(polygon.Holes[i], pivot, sin, cos);
		}

		private static void RotateRing(float2[] ring, float2 pivot, float sin, float cos)
		{
			for (int i = 0; i < ring.Length; i++)
			{
				var local = ring[i] - pivot;
				ring[i] = pivot + new float2(local.x * cos - local.y * sin, local.x * sin + local.y * cos);
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();
			var outerColor = gizmosOptions.Color;
			var holeColor = new Color(outerColor.r, outerColor.g, outerColor.b, outerColor.a * 0.5f);

			Gizmos.matrix = transform.localToWorldMatrix;
			RegionGizmoUtility.Draw(Blocks.Value, outerColor, holeColor);
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
