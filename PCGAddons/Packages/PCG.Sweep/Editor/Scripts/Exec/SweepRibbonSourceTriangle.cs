using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepRibbonSourceTriangle
	{
		internal int NetworkComponent;
		internal int SourceOrder;
		internal bool Strip;
		internal float2 A;
		internal float2 B;
		internal float2 C;
		internal float3 WorldA;
		internal float3 WorldB;
		internal float3 WorldC;
		internal float2 UvA;
		internal float2 UvB;
		internal float2 UvC;
		internal bool TerrainOutOfBounds;
	}
}
