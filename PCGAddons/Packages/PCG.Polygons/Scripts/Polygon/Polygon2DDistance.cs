using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed partial class Polygon2D
	{
		public float DistanceToBoundarySq(float2 point)
		{
			float best = float.MaxValue;
			ScanBoundaryRing(Outer, point, ref best);
			for (int i = 0; i < Holes.Count; i++)
			{
				ScanBoundaryRing(Holes[i], point, ref best);
			}

			return best;
		}

		private static void ScanBoundaryRing(float2[] ring, float2 point, ref float best)
		{
			if (ring == null || ring.Length < 2)
				return;

			for (int i = 0; i < ring.Length; i++)
			{
				var a = ring[i];
				var b = ring[(i + 1) % ring.Length];
				var c = ClosestOnSegment(a, b, point);
				float d = math.distancesq(c, point);
				if (d < best)
					best = d;
			}
		}

		private static float2 ClosestOnSegment(float2 a, float2 b, float2 point)
		{
			var ab = b - a;
			float len = math.lengthsq(ab);
			if (len < 1e-8f)
				return a;

			float t = math.clamp(math.dot(point - a, ab) / len, 0f, 1f);
			return a + ab * t;
		}
	}
}
