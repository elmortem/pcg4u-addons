using System;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepJunctionPlanComponent
	{
		internal float2[] Outer = Array.Empty<float2>();
		internal float2[][] Holes = Array.Empty<float2[]>();
		internal int[] OuterEdgePortalArms = Array.Empty<int>();
		internal int[][] HoleEdgePortalArms = Array.Empty<int[]>();
	}
}
