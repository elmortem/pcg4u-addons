using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed class RegionMeshPlan
	{
		public List<Polygon2D> Merged;
		public MeshQuadtree Tree;
		public float PlaneY;
		public Func<float2, float> HeightSampler;
		public float HeightOffset;
		public float UvScale;
		public HashSet<(int Depth, int Ix, int Iz)> BoundaryBranch;
		public List<(int Ix, int Iz)> BoundaryRoots;
		public bool FlatPath;
		public List<float2[]> FlatTriangles;
	}
}
