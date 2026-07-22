using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepRectangleSourceTriangle
	{
		internal int NetworkComponent;
		internal int SourceOrder;
		internal bool Strip;
		internal float2 A;
		internal float2 B;
		internal float2 C;
		internal float3 BottomA;
		internal float3 BottomB;
		internal float3 BottomC;
		internal float3 TopA;
		internal float3 TopB;
		internal float3 TopC;
		internal float2 BottomUvA;
		internal float2 BottomUvB;
		internal float2 BottomUvC;
		internal float2 TopUvA;
		internal float2 TopUvB;
		internal float2 TopUvC;
		internal bool TerrainOutOfBounds;
	}
}
