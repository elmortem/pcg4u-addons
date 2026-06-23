using System.Collections.Generic;
using System.Threading;
using Clipper2ZLib;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
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

			var byDepth = RoadPolylineBuilder.CollectByDepth(input);
			var parts = new List<Polygon2D>();
			var joinType = ToJoinType(Data.Join);
			var endType = ToEndType(Data.Cap);

			using (var scope = OperationScope.Start(this))
			{
				foreach (var pair in byDepth)
				{
					var segments = pair.Value;
					float width = segments[0].Width;

					var openPaths = new List<float2[]>();
					var closedPaths = new List<float2[]>();
					RoadPolylineBuilder.Chain(segments, openPaths, closedPaths);

					var ribbons = PolygonClipper.InflatePolylines(openPaths, closedPaths, width * 0.5f, joinType, endType, Data.MiterLimit);
					parts.AddRange(ribbons);

					await scope.Step(ct: ct);
				}

				var roads = new RegionSet();
				roads.PlaneY = input.PlaneY;
				var merged = parts.Count > 0 ? PolygonClipper.Union(parts, new List<Polygon2D>()) : new List<Polygon2D>();
				for (int i = 0; i < merged.Count; i++)
					roads.AddRegion(merged[i]);

				Roads.Value = roads;
			}
		}

		private static JoinType ToJoinType(RoadJoinType join)
		{
			switch (join)
			{
				case RoadJoinType.Miter:
					return JoinType.Miter;
				case RoadJoinType.Square:
					return JoinType.Square;
				default:
					return JoinType.Round;
			}
		}

		private static EndType ToEndType(RoadCapType cap)
		{
			switch (cap)
			{
				case RoadCapType.Square:
					return EndType.Square;
				case RoadCapType.Round:
					return EndType.Round;
				default:
					return EndType.Butt;
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
