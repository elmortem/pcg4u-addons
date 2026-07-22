using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepRibbonFootprint
	{
		private readonly float2[] _outer;
		private readonly float2[] _inner;
		private readonly float2 _min;
		private readonly float2 _max;

		public int SplineIndex;

		internal SweepRibbonFootprint(SweepBoundaryCurve left, SweepBoundaryCurve right)
		{
			SplineIndex = left.SplineIndex;

			if (left.Closed)
			{
				_outer = BuildRing(left);
				_inner = BuildRing(right);
			}
			else
			{
				int count = left.Plan.Length + right.Plan.Length;
				_outer = new float2[count];
				for (int i = 0; i < left.Plan.Length; i++)
					_outer[i] = left.Plan[i];
				for (int i = 0; i < right.Plan.Length; i++)
					_outer[left.Plan.Length + i] = right.Plan[right.Plan.Length - 1 - i];
				_inner = null;
			}

			_min = new float2(float.MaxValue, float.MaxValue);
			_max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < _outer.Length; i++)
			{
				_min = math.min(_min, _outer[i]);
				_max = math.max(_max, _outer[i]);
			}
			if (_inner != null)
			{
				for (int i = 0; i < _inner.Length; i++)
				{
					_min = math.min(_min, _inner[i]);
					_max = math.max(_max, _inner[i]);
				}
			}
		}

		internal bool Contains(float2 point)
		{
			if (point.x < _min.x || point.x > _max.x || point.y < _min.y || point.y > _max.y)
				return false;

			bool outer = Inside(_outer, point);
			if (_inner == null)
				return outer;

			return outer != Inside(_inner, point);
		}

		private static float2[] BuildRing(SweepBoundaryCurve curve)
		{
			int count = curve.Plan.Length;
			if (count > 1 && math.distancesq(curve.Plan[0], curve.Plan[count - 1]) < 1e-12f)
				count--;

			var ring = new float2[count];
			for (int i = 0; i < count; i++)
				ring[i] = curve.Plan[i];
			return ring;
		}

		private static bool Inside(float2[] ring, float2 point)
		{
			bool inside = false;
			int count = ring.Length;
			for (int i = 0, j = count - 1; i < count; j = i++)
			{
				float2 a = ring[i];
				float2 b = ring[j];
				if (a.y > point.y != b.y > point.y)
				{
					float x = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
					if (point.x < x)
						inside = !inside;
				}
			}
			return inside;
		}
	}
}
