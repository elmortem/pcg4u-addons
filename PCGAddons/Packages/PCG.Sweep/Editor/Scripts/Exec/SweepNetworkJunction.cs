using Unity.Mathematics;

namespace PCG.Sweep
{
	public sealed class SweepNetworkJunction
	{
		public int[] SourceJunctionIndices;
		public float3 Center;
		public float3 Axis;
		public float3 E1;
		public float3 E2;
		public SweepNetworkArm[] Arms;
	}
}
