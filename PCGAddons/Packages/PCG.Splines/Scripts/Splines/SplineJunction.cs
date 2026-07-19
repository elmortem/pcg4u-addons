using System;
using Unity.Mathematics;

namespace PCG.Splines
{
	[Serializable]
	public struct SplineJunction
	{
		public float3 Position;
		public int Valency;
	}
}
