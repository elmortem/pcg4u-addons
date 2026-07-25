using System.Collections.Generic;
using System.Threading;
using Clipper2ZLib;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Splines;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Polygons.City
{
	public class BlocksToRoadsNodeExecutor : PcgAsyncPreviewNodeExecutor<BlocksToRoadsNode>
	{
		public PcgOutput<RegionSet> Roads;
		public PcgOutput<List<Spline>> Centerlines;

		public override bool IsEmpty => Roads.Value == null && Centerlines.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Roads.Value = new RegionSet();
			Centerlines.Value = new List<Spline>();

			var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Blocks), ct);
			if (input == null)
				return;

			var joinType = ToJoinType(Data.Join);
			var endType = ToEndType(Data.Cap);
			float miterLimit = Data.MiterLimit;

			var computed = await PcgWorkerScheduler.RunAsync(() =>
			{
				var byDepth = RoadPolylineBuilder.CollectByDepth(input);
				RoadPolylineBuilder.PruneShortDeadEnds(byDepth, Data.MinimumDeadEndLength);
				var parts = new List<Polygon2D>();
				var centerlines = new List<CenterlineData>();
				foreach (var pair in byDepth)
				{
					ct.ThrowIfCancellationRequested();
					var segments = pair.Value;
					if (segments == null || segments.Count == 0)
						continue;
					float width = segments[0].Width;

					var openPaths = new List<float2[]>();
					var closedPaths = new List<float2[]>();
					RoadPolylineBuilder.Chain(segments, openPaths, closedPaths);
					AddCenterlineData(openPaths, false, width, centerlines);
					AddCenterlineData(closedPaths, true, width, centerlines);

					var ribbons = PolygonClipper.InflatePolylines(openPaths, closedPaths, width * 0.5f, joinType, endType, miterLimit);
					parts.AddRange(ribbons);
				}

				var roads = new RegionSet
				{
					PlaneY = input.PlaneY
				};
				var merged = parts.Count > 0 ? PolygonClipper.Union(parts, new List<Polygon2D>()) : new List<Polygon2D>();
				for (int i = 0; i < merged.Count; i++)
					roads.AddRegion(merged[i]);

				return new RoadComputeResult(roads, centerlines);
			}, ct);

			var centerlineSplines = new List<Spline>(computed.Centerlines.Count);
			for (int i = 0; i < computed.Centerlines.Count; i++)
				AddCenterline(computed.Centerlines[i], input.PlaneY, centerlineSplines);

			Roads.Value = computed.Roads;
			Centerlines.Value = centerlineSplines;
		}

		private static void AddCenterlineData(List<float2[]> paths, bool closed, float width, List<CenterlineData> results)
		{
			for (int p = 0; p < paths.Count; p++)
			{
				var path = paths[p];
				if (path == null || path.Length < 2)
					continue;
				results.Add(new CenterlineData(path, closed, width));
			}
		}

		private static void AddCenterline(CenterlineData data, float planeY, List<Spline> results)
		{
			var spline = new Spline
			{
				Closed = data.Closed
			};

			for (int i = 0; i < data.Path.Length; i++)
			{
				var point = data.Path[i];
				spline.Add(new BezierKnot(new float3(point.x, planeY, point.y)), TangentMode.Linear);
			}

			SplineWidthUtility.SetConstant(spline, data.Width);
			results.Add(spline);
		}

		private readonly struct CenterlineData
		{
			public readonly float2[] Path;
			public readonly bool Closed;
			public readonly float Width;

			public CenterlineData(float2[] path, bool closed, float width)
			{
				Path = path;
				Closed = closed;
				Width = width;
			}
		}

		private sealed class RoadComputeResult
		{
			public readonly RegionSet Roads;
			public readonly List<CenterlineData> Centerlines;

			public RoadComputeResult(RegionSet roads, List<CenterlineData> centerlines)
			{
				Roads = roads;
				Centerlines = centerlines;
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
