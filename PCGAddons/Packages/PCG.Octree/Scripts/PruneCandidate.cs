using PCG.Points;
using Unity.Mathematics;

namespace PCG.Octree
{
	public struct PruneCandidate
	{
		public float3 Position;
		public float Radius;
		public int Port;
		public PcgPointCloud Cloud;
		public int Index;
	}
}
