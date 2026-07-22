using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepBooleanProfile
	{
		public float2[] Points;
		public bool[] KeepEdges;
		public float[] EdgeU0;
		public float[] EdgeU1;
	}
}
