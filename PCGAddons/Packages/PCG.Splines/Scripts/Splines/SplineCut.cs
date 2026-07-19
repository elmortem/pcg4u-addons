using System;
using Unity.Mathematics;

namespace PCG.Splines
{
	[Serializable]
	public struct SplineCut
	{
		public int SplineIndex;
		public int CurveIndex;
		public float CurveT;
		public float Distance;
		public float3 Position;
		public int JunctionIndex;
	}
}
