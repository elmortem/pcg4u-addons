using Unity.Mathematics;

namespace PCG.Splines
{
	public struct NetworkSegment
	{
		public int SplineIndex;
		public int CurveIndex;
		public float T0;
		public float T1;
		public float2 A;
		public float2 B;
		public float Y0;
		public float Y1;
		public float2 Min;
		public float2 Max;
	}
}
