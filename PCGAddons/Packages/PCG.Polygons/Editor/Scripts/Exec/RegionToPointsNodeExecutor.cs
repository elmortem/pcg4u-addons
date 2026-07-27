using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class RegionToPointsNodeExecutor : PcgAsyncPreviewNodeExecutor<RegionToPointsNode>, IPointsCount
	{
		public PcgOutput<PcgPointCloud> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (input == null)
			{
				Results.Value = new PcgPointCloud();
				return;
			}

			var roads = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Roads), ct);
			var exclusions = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.ExclusionRegions), ct);
			var count = GetInputValue(nameof(Data.Count), Data.Count);
			var spacing = GetInputValue(nameof(Data.Spacing), Data.Spacing);
			var gridJitter = GetInputValue(nameof(Data.GridJitter), Data.GridJitter);
			var margin = GetInputValue(nameof(Data.Margin), Data.Margin);
			var footprintClearance = GetInputValue(nameof(Data.FootprintClearance), Data.FootprintClearance);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);
			var mode = Data.Mode;
			var edgeSource = roads != null && roads.Count > 0 ? roads : input;

			var work = PcgWorkerScheduler.RunAsync(() =>
			{
				var output = new List<PointData>(input.Count);
				var sourceRegionRow = new List<int>(input.Count);
				var noBuildZones = exclusions != null && exclusions.Count > 0
					? PolygonClipper.Inflate(exclusions.Regions, math.max(0f, footprintClearance))
					: null;
				for (int i = 0; i < input.Regions.Count; i++)
				{
					ct.ThrowIfCancellationRequested();
					var pieces = Inset(input.Regions[i], margin);
					if (noBuildZones != null && noBuildZones.Count > 0)
						pieces = PolygonClipper.Difference(pieces, noBuildZones);
					if (pieces.Count == 0)
						continue;

					int start = output.Count;
					switch (mode)
					{
						case RegionToPointsMode.Centroid:
							AddCentroid(output, pieces, input.PlaneY);
							break;
						case RegionToPointsMode.Random:
							RegionFill.FillRandomBlocking(output, pieces, input.PlaneY, count, seed + i, ct);
							break;
						case RegionToPointsMode.Grid:
							RegionFill.FillGridBlocking(output, pieces, input.PlaneY, spacing, gridJitter, seed + i, ct);
							break;
					}

					for (int k = start; k < output.Count; k++)
						sourceRegionRow.Add(i);
				}

				OrientToNearestEdge(output, edgeSource);
				return (output, sourceRegionRow);
			}, ct);
			while (work.Status == UniTaskStatus.Pending)
			{
				PcgComputeSystem.ReportProgress(this);
				await UniTask.Delay(250, cancellationToken: ct);
			}
			var (output, sourceRegionRow) = await work;

			var cloud = new PcgPointCloud(output.Count);
			for (int k = 0; k < output.Count; k++)
			{
				cloud.Points.Add(output[k]);
				cloud.Attributes.AppendRow(input.Attributes, sourceRegionRow[k]);
			}

			var regionIndexColumn = cloud.Attributes.EnsureColumn<int>(CityAttributes.RegionIndex);
			for (int k = 0; k < output.Count; k++)
			{
				regionIndexColumn.Values[k] = sourceRegionRow[k];
			}

			Results.Value = cloud;
		}

		private static List<Polygon2D> Inset(Polygon2D polygon, float margin)
		{
			var single = new List<Polygon2D> { polygon };
			if (margin <= 0f)
				return single;

			return PolygonClipper.Inflate(single, -margin);
		}

		private static void AddCentroid(List<PointData> results, IList<Polygon2D> pieces, float planeY)
		{
			if (!TryAreaCentroid(pieces, out var center))
				return;

			if (!RegionFill.ContainsAny(pieces, center))
				return;

			results.Add(new PointData
			{
				Position = new float3(center.x, planeY, center.y),
				Normal = new float3(0f, 1f, 0f),
				Scale = 1f
			});
		}

		private static bool TryAreaCentroid(IList<Polygon2D> pieces, out float2 centroid)
		{
			centroid = float2.zero;
			double area2 = 0.0;
			double cx = 0.0;
			double cy = 0.0;

			for (int i = 0; i < pieces.Count; i++)
			{
				AccumulateRing(pieces[i].Outer, ref area2, ref cx, ref cy);
				for (int h = 0; h < pieces[i].Holes.Count; h++)
					AccumulateRing(pieces[i].Holes[h], ref area2, ref cx, ref cy);
			}

			if (math.abs(area2) < 1e-9)
				return false;

			double inv = 1.0 / (3.0 * area2);
			centroid = new float2((float)(cx * inv), (float)(cy * inv));
			return true;
		}

		private static void AccumulateRing(float2[] ring, ref double area2, ref double cx, ref double cy)
		{
			if (ring == null || ring.Length < 3)
				return;

			int n = ring.Length;
			for (int i = 0; i < n; i++)
			{
				var p0 = ring[i];
				var p1 = ring[(i + 1) % n];
				double cross = (double)p0.x * p1.y - (double)p1.x * p0.y;
				area2 += cross;
				cx += (p0.x + p1.x) * cross;
				cy += (p0.y + p1.y) * cross;
			}
		}

		private static void OrientToNearestEdge(List<PointData> results, RegionSet edges)
		{
			if (edges == null || edges.Regions.Count <= 0)
				return;

			for (int i = 0; i < results.Count; i++)
			{
				var point = results[i];
				var p = new float2(point.Position.x, point.Position.z);
				if (!TryNearestEdgePoint(edges, p, out var nearest))
					continue;

				var dir = nearest - p;
				if (math.lengthsq(dir) < 1e-8f)
					continue;

				point.Angle = math.degrees(math.atan2(dir.x, dir.y));
				results[i] = point;
			}
		}

		private static bool TryNearestEdgePoint(RegionSet edges, float2 p, out float2 nearest)
		{
			nearest = p;
			float best = float.MaxValue;
			bool found = false;

			for (int r = 0; r < edges.Regions.Count; r++)
			{
				var region = edges.Regions[r];
				ScanRing(region.Outer, p, ref best, ref nearest, ref found);
				for (int h = 0; h < region.Holes.Count; h++)
					ScanRing(region.Holes[h], p, ref best, ref nearest, ref found);
			}

			return found;
		}

		private static void ScanRing(float2[] ring, float2 p, ref float best, ref float2 nearest, ref bool found)
		{
			if (ring == null || ring.Length < 2)
				return;

			for (int i = 0; i < ring.Length; i++)
			{
				var a = ring[i];
				var b = ring[(i + 1) % ring.Length];
				var c = ClosestOnSegment(a, b, p);
				float d = math.distancesq(c, p);
				if (d < best)
				{
					best = d;
					nearest = c;
					found = true;
				}
			}
		}

		private static float2 ClosestOnSegment(float2 a, float2 b, float2 p)
		{
			var ab = b - a;
			float len = math.lengthsq(ab);
			if (len < 1e-8f)
				return a;

			float t = math.clamp(math.dot(p - a, ab) / len, 0f, 1f);
			return a + ab * t;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();
			GizmosUtility.DrawPoints(this, Results.Value, gizmosOptions, transform);
		}
	}
}
