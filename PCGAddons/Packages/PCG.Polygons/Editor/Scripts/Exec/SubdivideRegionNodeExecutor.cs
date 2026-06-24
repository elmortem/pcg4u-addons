using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class SubdivideRegionNodeExecutor : PcgAsyncPreviewNodeExecutor<SubdivideRegionNode>
	{
		public PcgOutput<RegionSet> Blocks;

		public override bool IsEmpty => Blocks.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var result = new RegionSet();
			Blocks.Value = result;

			var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (input == null)
				return;

			result.PlaneY = input.PlaneY;

			var minSize = GetInputValue(nameof(Data.MinSize), Data.MinSize);
			var maxDepth = GetInputValue(nameof(Data.MaxDepth), Data.MaxDepth);
			var splitJitter = GetInputValue(nameof(Data.SplitJitter), Data.SplitJitter);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);

			var random = PcgRandom.Create(seed);
			var queue = new Queue<(Polygon2D polygon, int depth)>();
			for (int i = 0; i < input.Regions.Count; i++)
				queue.Enqueue((input.Regions[i], 0));

			var left = new List<Polygon2D>();
			var right = new List<Polygon2D>();

			using (var scope = OperationScope.Start(this))
			{
				while (queue.Count > 0)
				{
					var (polygon, depth) = queue.Dequeue();
					polygon.GetBounds(out var min, out var max);
					var size = max - min;
					float maxDim = math.max(size.x, size.y);

					if (maxDim < minSize || depth >= maxDepth)
					{
						int row = result.AddRegion(polygon);
						result.Attributes.Set(CityAttributes.Depth, row, depth);
						await scope.Step(ct: ct);
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
						queue.Enqueue((left[i], depth + 1));
					for (int i = 0; i < right.Count; i++)
						queue.Enqueue((right[i], depth + 1));

					await scope.Step(ct: ct);
				}
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
