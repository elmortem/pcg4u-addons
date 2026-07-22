using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepBoundaryHit
	{
		public int Index;
		public int CurveA;
		public int CurveB;
		public int SegmentA;
		public int SegmentB;
		public float ParamA;
		public float ParamB;
		public float StationA;
		public float StationB;
		public float2 Plan;
		public float3 Point;
		public int Cluster;

		public int CurveOf(int slot)
		{
			return slot == 0 ? CurveA : CurveB;
		}

		public float StationOf(int slot)
		{
			return slot == 0 ? StationA : StationB;
		}
	}
}
