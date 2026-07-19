using Unity.Mathematics;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public static class SplineNetworkMath
	{
		public static float2 Xz(float3 p)
		{
			return new float2(p.x, p.z);
		}

		public static BezierCurve SubCurve(BezierCurve curve, float t0, float t1)
		{
			t0 = math.clamp(t0, 0f, 1f);
			t1 = math.clamp(t1, 0f, 1f);
			if (t1 <= t0)
				return new BezierCurve(curve.P0, curve.P0, curve.P0, curve.P0);

			if (t0 <= 0f && t1 >= 1f)
				return curve;

			BezierCurve left;
			if (t1 >= 1f)
				left = curve;
			else
				CurveUtility.Split(curve, t1, out left, out _);

			if (t0 <= 0f)
				return left;

			var localT = t0 / t1;
			CurveUtility.Split(left, localT, out _, out var right);
			return right;
		}

		public static float PartialLength(BezierCurve curve, float t)
		{
			if (t <= 0f)
				return 0f;

			var sub = SubCurve(curve, 0f, t);
			return CurveUtility.CalculateLength(sub);
		}

		public static float ChordErrorXz(BezierCurve curve)
		{
			var p0 = Xz(curve.P0);
			var p3 = Xz(curve.P3);
			var dir = p3 - p0;
			var len = math.length(dir);

			if (len < 1e-6f)
			{
				var e0 = math.length(Xz(curve.P1) - p0);
				var e1 = math.length(Xz(curve.P2) - p0);
				return math.max(e0, e1);
			}

			var normal = new float2(-dir.y, dir.x) / len;
			var d1 = math.abs(math.dot(Xz(curve.P1) - p0, normal));
			var d2 = math.abs(math.dot(Xz(curve.P2) - p0, normal));
			return math.max(d1, d2);
		}

		public static bool SegmentsIntersectXz(float2 a0, float2 a1, float2 b0, float2 b1, out float ta, out float tb)
		{
			ta = 0f;
			tb = 0f;

			var d1 = a1 - a0;
			var d2 = b1 - b0;
			var denom = d1.x * d2.y - d1.y * d2.x;

			var scale = 1e-6f * math.length(d1) * math.length(d2);
			if (math.abs(denom) <= scale)
				return false;

			var diff = b0 - a0;
			ta = (diff.x * d2.y - diff.y * d2.x) / denom;
			tb = (diff.x * d1.y - diff.y * d1.x) / denom;

			return ta >= 0f && ta <= 1f && tb >= 0f && tb <= 1f;
		}

		public static float SegmentDistanceSqXz(float2 a0, float2 a1, float2 b0, float2 b1)
		{
			if (SegmentsIntersectXz(a0, a1, b0, b1, out _, out _))
				return 0f;

			var d = math.min(
				math.min(PointSegmentDistanceSq(a0, b0, b1), PointSegmentDistanceSq(a1, b0, b1)),
				math.min(PointSegmentDistanceSq(b0, a0, a1), PointSegmentDistanceSq(b1, a0, a1)));
			return d;
		}

		public static float PointSegmentDistanceSq(float2 p, float2 s0, float2 s1)
		{
			var dir = s1 - s0;
			var lenSq = math.lengthsq(dir);
			if (lenSq < 1e-12f)
				return math.lengthsq(p - s0);

			var t = math.clamp(math.dot(p - s0, dir) / lenSq, 0f, 1f);
			var closest = s0 + dir * t;
			return math.lengthsq(p - closest);
		}
	}
}
