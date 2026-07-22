using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepBoundaryCurve
	{
		public int SplineIndex;
		public int Side;
		public float3[] Points;
		public float2[] Plan;
		public float[] Station;
		public bool Closed;

		public int SegmentCount => Points.Length - 1;

		public float StationAt(int segment, float t)
		{
			return math.lerp(Station[segment], Station[segment + 1], t);
		}

		public float3 PointAt(int segment, float t)
		{
			return math.lerp(Points[segment], Points[segment + 1], t);
		}

		public float2 PlanAt(int segment, float t)
		{
			return math.lerp(Plan[segment], Plan[segment + 1], t);
		}

		public bool TryLocate(float station, out int segment, out float t)
		{
			segment = 0;
			t = 0f;

			int count = Station.Length;
			if (count < 2)
				return false;

			if (station <= Station[0])
				return true;

			if (station >= Station[count - 1])
			{
				segment = count - 2;
				t = 1f;
				return true;
			}

			int low = 0;
			int high = count - 1;
			while (high - low > 1)
			{
				int mid = (low + high) / 2;
				if (Station[mid] <= station)
					low = mid;
				else
					high = mid;
			}

			segment = low;
			float span = Station[low + 1] - Station[low];
			t = span > 1e-6f ? (station - Station[low]) / span : 0f;
			return true;
		}
	}
}
