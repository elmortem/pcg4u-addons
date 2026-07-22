using Unity.Mathematics;

namespace PCG.Sweep
{
	public sealed class SweepNetworkArm
	{
		public int SourceJunctionIndex;
		public int PieceIndex;
		public bool AtPieceStart;
		public float Azimuth;
		public float3 Outward;
		public SweepFrame Frame;
		public float3 Right;
		public float3 Up;
		public float3 EdgeDir;
		public float WidthMul;
		public SweepFrame[] ApproachFrames;
		public float3[] ApproachRights;
		public float3[] ApproachUps;
		public SweepNetworkArmRole Role;
		public bool Terminal;
	}
}
