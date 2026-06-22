using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class BlocksToRoadsNodeExecutor : PcgAsyncPreviewNodeExecutor<BlocksToRoadsNode>
	{
		public PcgOutput<RegionSet> Roads;

		public override bool IsEmpty => Roads.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Roads.Value = new RegionSet();

			var input = GetInputValue(nameof(Data.Blocks), Data.Blocks);
			if (input == null)
				return;

			var strips = new List<Polygon2D>();

			using (var scope = OperationScope.Start(this))
			{
				foreach (var polygon in input.Regions)
				{
					if (!polygon.HasEdgeData() || !polygon.EdgeAttributes.HasColumn(CityAttributes.Width))
						continue;

					for (int e = 0; e < polygon.Outer.Length; e++)
					{
						float width = polygon.GetEdge<float>(CityAttributes.Width, e);
						if (width <= 0f)
							continue;

						var a = polygon.Outer[e];
						var b = polygon.Outer[(e + 1) % polygon.Outer.Length];
						var strip = PolygonEdgeClip.BuildStrip(a, b, width);
						if (strip != null)
							strips.Add(strip);

						await scope.Step(ct: ct);
					}
				}

				var roads = new RegionSet();
				roads.PlaneY = input.PlaneY;
				var merged = strips.Count > 0 ? PolygonClipper.Union(strips, new List<Polygon2D>()) : new List<Polygon2D>();
				for (int i = 0; i < merged.Count; i++)
					roads.AddRegion(merged[i]);

				Roads.Value = roads;
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();
			var outerColor = gizmosOptions.Color;
			var holeColor = new Color(outerColor.r, outerColor.g, outerColor.b, outerColor.a * 0.5f);

			Gizmos.matrix = transform.localToWorldMatrix;
			RegionGizmoUtility.Draw(Roads.Value, outerColor, holeColor);
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
