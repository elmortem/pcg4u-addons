using PCG.Polygons;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepRibbonNetworkDomainComponent
	{
		internal int NetworkComponent;
		internal Polygon2D Polygon;
		internal float2[] PlanVertices;
		internal int[] Triangles;
		internal SweepRibbonBoundaryKind[] OuterEdgeKinds;
		internal SweepRibbonBoundaryKind[][] HoleEdgeKinds;
		internal SweepRibbonSourceTriangle[] Sources;
		internal bool TerrainOutOfBounds;
	}
}
