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
	public class LotsFromBlockNodeExecutor : PcgAsyncPreviewNodeExecutor<LotsFromBlockNode>
	{
		public PcgOutput<RegionSet> Lots;

		public override bool IsEmpty => Lots.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Lots.Value = new RegionSet();

			var input = GetInputValue(nameof(Data.Blocks), Data.Blocks);
			if (input == null)
				return;

			var lotWidth = GetInputValue(nameof(Data.LotWidth), Data.LotWidth);

			var result = new RegionSet();
			result.PlaneY = input.PlaneY;
			int lotId = 0;

			var subject = new List<Polygon2D>(1) { null };
			var clip = new List<Polygon2D>(1) { null };

			using (var scope = OperationScope.Start(this))
			{
				foreach (var block in input.Regions)
				{
					float2 dir = LongestEdgeDir(block);
					float2 normal = new float2(-dir.y, dir.x);
					ProjectRange(block, dir, out float minT, out float maxT);
					ProjectRange(block, normal, out float minN, out float maxN);

					float span = maxT - minT;
					int count = math.max(1, (int)math.round(span / lotWidth));
					float step = span / count;

					subject[0] = block;

					for (int i = 0; i < count; i++)
					{
						float t0 = minT + i * step;
						float t1 = minT + (i + 1) * step;
						clip[0] = BuildAlignedRect(dir, normal, t0, t1, minN - 1f, maxN + 1f);

						var lots = PolygonEdgeClip.Intersection(subject, clip, null);
						for (int j = 0; j < lots.Count; j++)
						{
							int row = result.AddRegion(lots[j]);
							result.Attributes.Set(CityAttributes.LotId, row, lotId);
							lotId++;
						}

						await scope.Step(ct: ct);
					}
				}
			}

			Lots.Value = result;
		}

		private static float2 LongestEdgeDir(Polygon2D polygon)
		{
			var outer = polygon.Outer;
			float best = -1f;
			float2 dir = new float2(1f, 0f);
			for (int i = 0; i < outer.Length; i++)
			{
				var a = outer[i];
				var b = outer[(i + 1) % outer.Length];
				var edge = b - a;
				float len = math.lengthsq(edge);
				if (len > best)
				{
					best = len;
					dir = edge;
				}
			}

			float l = math.length(dir);
			if (l < 1e-5f)
				return new float2(1f, 0f);

			return dir / l;
		}

		private static void ProjectRange(Polygon2D polygon, float2 axis, out float min, out float max)
		{
			var outer = polygon.Outer;
			min = float.MaxValue;
			max = float.MinValue;
			for (int i = 0; i < outer.Length; i++)
			{
				float p = math.dot(outer[i], axis);
				min = math.min(min, p);
				max = math.max(max, p);
			}
		}

		private static Polygon2D BuildAlignedRect(float2 dir, float2 normal, float t0, float t1, float n0, float n1)
		{
			var polygon = new Polygon2D();
			polygon.Outer = new[]
			{
				dir * t0 + normal * n0,
				dir * t1 + normal * n0,
				dir * t1 + normal * n1,
				dir * t0 + normal * n1
			};

			return polygon;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();
			var outerColor = gizmosOptions.Color;
			var holeColor = new Color(outerColor.r, outerColor.g, outerColor.b, outerColor.a * 0.5f);

			Gizmos.matrix = transform.localToWorldMatrix;
			RegionGizmoUtility.Draw(Lots.Value, outerColor, holeColor);
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
