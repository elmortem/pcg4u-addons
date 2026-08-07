using System.Threading;
using PCG.Points;
using Unity.Mathematics;

namespace PCG.Polygons.City
{
	public static class LotFrontage
	{
		private const float TieTolerance = 0.5f;
		private const float CollinearSinTolerance = 0.07f;
		private const float InsideProbe = 0.1f;
		private const float ChordProbe = 0.05f;
		private const float ChordStep = 0.5f;
		private const float MaxChord = 16f;
		private const float WidthTolerance = 0.05f;

		public static PcgPointCloud Build(RegionSet lots, RegionSet roads, LotFrontageSettings settings, CancellationToken ct)
		{
			var cloud = new PcgPointCloud(lots?.Count ?? 0);
			if (lots == null || lots.Count == 0 || roads == null || roads.Count == 0)
				return cloud;

			for (int i = 0; i < lots.Regions.Count; i++)
			{
				ct.ThrowIfCancellationRequested();
				if (!TryCompute(lots.Regions[i], i, roads, settings, out var result))
					continue;

				cloud.Points.Add(new PointData
				{
					Position = new float3(result.Position.x, lots.PlaneY, result.Position.y),
					Normal = new float3(0f, 1f, 0f),
					Angle = result.AngleDegrees,
					Scale = 1f
				});

				int row = cloud.Points.Count - 1;
				cloud.Attributes.AppendRow(lots.Attributes, i);

				int lotId = lots.Attributes.HasColumn(CityAttributes.LotId)
					? lots.Attributes.Get<int>(CityAttributes.LotId, i)
					: i;
				cloud.Attributes.Set(CityAttributes.LotId, row, lotId);
				cloud.Attributes.Set(CityAttributes.LotArea, row, result.LotArea);
				cloud.Attributes.Set(CityAttributes.LotWidth, row, result.FrontageLength);
				cloud.Attributes.Set(CityAttributes.RoadClass, row, result.RoadClass);
			}

			return cloud;
		}

		public static bool TryCompute(Polygon2D lot, int lotIndex, RegionSet roads, LotFrontageSettings settings, out LotFrontageResult result)
		{
			result = default;
			if (lot?.Outer == null || lot.Outer.Length < 3 || roads == null || roads.Count == 0)
				return false;

			var outline = MergeCollinear(lot.Outer);
			if (outline.Length < 3)
				return false;

			int n = outline.Length;
			float bestDist = float.MaxValue;
			var edgeDist = new float[n];
			var edgeLen = new float[n];

			for (int e = 0; e < n; e++)
			{
				var a = outline[e];
				var b = outline[(e + 1) % n];
				var mid = (a + b) * 0.5f;
				edgeLen[e] = math.distance(a, b);
				edgeDist[e] = NearestRoadPoint(roads, mid, out _, out _);
				if (edgeDist[e] < bestDist)
					bestDist = edgeDist[e];
			}

			if (bestDist > settings.MaxRoadDistance)
				return false;

			int frontal = -1;
			float frontalLen = -1f;
			for (int e = 0; e < n; e++)
			{
				if (edgeDist[e] - bestDist > TieTolerance)
					continue;

				if (edgeLen[e] > frontalLen)
				{
					frontalLen = edgeLen[e];
					frontal = e;
				}
			}

			if (frontal < 0 || frontalLen < settings.MinFrontage)
				return false;

			var fa = outline[frontal];
			var fb = outline[(frontal + 1) % n];
			var frontMid = (fa + fb) * 0.5f;
			var dir = math.normalize(fb - fa);
			var perp = new float2(-dir.y, dir.x);

			float2 inward;
			if (lot.Contains(frontMid + perp * InsideProbe))
				inward = perp;
			else if (lot.Contains(frontMid - perp * InsideProbe))
				inward = -perp;
			else
				return false;

			float setback = settings.Setback;
			if (settings.SetbackJitter > 0f)
			{
				var rng = Random.CreateFromIndex(math.hash(new int2(settings.Seed, lotIndex)));
				setback += rng.NextFloat(-settings.SetbackJitter, settings.SetbackJitter);
			}

			var outward = -inward;
			NearestRoadPoint(roads, frontMid, out int roadRegion, out var roadPoint);

			var position = frontMid + inward * setback;
			float placementDist = NearestRoadPoint(roads, position, out _, out _);
			if (settings.MinPlacementClearance > 0f && placementDist < settings.MinPlacementClearance)
				return false;
			if (settings.MaxPlacementDistance > 0f && placementDist > settings.MaxPlacementDistance)
				return false;

			result = new LotFrontageResult
			{
				Position = position,
				AngleDegrees = math.degrees(math.atan2(outward.x, outward.y)),
				FrontageLength = frontalLen,
				LotArea = Area(lot),
				RoadDistance = edgeDist[frontal],
				RoadClass = EstimateRoadClass(frontMid, roads, roadRegion, roadPoint)
			};
			return true;
		}

		public static float2[] MergeCollinear(float2[] ring)
		{
			if (ring == null || ring.Length < 3)
				return ring;

			int n = ring.Length;
			var keep = new System.Collections.Generic.List<float2>(n);
			for (int i = 0; i < n; i++)
			{
				var prev = ring[(i - 1 + n) % n];
				var curr = ring[i];
				var next = ring[(i + 1) % n];
				var a = curr - prev;
				var b = next - curr;
				float lenA = math.length(a);
				float lenB = math.length(b);
				if (lenA < 1e-5f || lenB < 1e-5f)
					continue;

				float sin = math.abs(a.x * b.y - a.y * b.x) / math.max(1e-8f, lenA * lenB);
				if (sin < CollinearSinTolerance && math.dot(a, b) > 0f)
					continue;

				keep.Add(curr);
			}

			return keep.Count >= 3 ? keep.ToArray() : ring;
		}

		public static float Area(Polygon2D polygon)
		{
			double area = math.abs(RingArea(polygon.Outer));
			for (int h = 0; h < polygon.Holes.Count; h++)
				area -= math.abs(RingArea(polygon.Holes[h]));

			return (float)math.max(0.0, area);
		}

		private static double RingArea(float2[] ring)
		{
			if (ring == null || ring.Length < 3)
				return 0.0;

			double sum = 0.0;
			for (int i = 0; i < ring.Length; i++)
			{
				var p0 = ring[i];
				var p1 = ring[(i + 1) % ring.Length];
				sum += (double)p0.x * p1.y - (double)p1.x * p0.y;
			}

			return sum * 0.5;
		}

		private static float NearestRoadPoint(RegionSet roads, float2 p, out int regionIndex, out float2 nearest)
		{
			regionIndex = -1;
			nearest = p;
			float best = float.MaxValue;

			for (int r = 0; r < roads.Regions.Count; r++)
			{
				var region = roads.Regions[r];
				if (ScanRing(region.Outer, p, ref best, ref nearest))
					regionIndex = r;

				for (int h = 0; h < region.Holes.Count; h++)
				{
					if (ScanRing(region.Holes[h], p, ref best, ref nearest))
						regionIndex = r;
				}
			}

			return regionIndex >= 0 ? math.sqrt(best) : float.MaxValue;
		}

		private static bool ScanRing(float2[] ring, float2 p, ref float best, ref float2 nearest)
		{
			if (ring == null || ring.Length < 2)
				return false;

			bool improved = false;
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
					improved = true;
				}
			}

			return improved;
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

		private static int EstimateRoadClass(float2 from, RegionSet roads, int regionIndex, float2 entry)
		{
			if (regionIndex < 0)
				return 2;

			var dir = entry - from;
			if (math.lengthsq(dir) < 1e-6f)
				return 2;

			dir = math.normalize(dir);
			var region = roads.Regions[regionIndex];
			if (!region.Contains(entry + dir * ChordProbe))
				return 2;

			float lo = ChordProbe;
			float hi = lo;
			while (hi < MaxChord && region.Contains(entry + dir * hi))
			{
				lo = hi;
				hi += ChordStep;
			}

			float width;
			if (hi >= MaxChord)
			{
				width = MaxChord;
			}
			else
			{
				for (int i = 0; i < 16; i++)
				{
					float t = (lo + hi) * 0.5f;
					if (region.Contains(entry + dir * t))
						lo = t;
					else
						hi = t;
				}

				width = (lo + hi) * 0.5f;
			}

			return ClassifyWidth(width);
		}

		private static int ClassifyWidth(float width)
		{
			if (width >= 5f - WidthTolerance)
				return 0;
			if (width >= 4f - WidthTolerance)
				return 1;
			if (width >= 2.5f - WidthTolerance)
				return 2;
			return 3;
		}
	}
}
