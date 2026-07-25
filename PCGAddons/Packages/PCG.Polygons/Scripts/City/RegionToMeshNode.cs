using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Instances;
using UnityEngine;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Builds a crack-free mesh draped over the terrain from regions.",
		DisplayName = "Region To Mesh",
		Category = "Polygons/City",
		Tags = new[] { "region", "mesh", "terrain", "instances" })]
	public sealed class RegionToMeshNode : PcgPreviewNode
	{
		[PcgMemberInfo("Whether the node produces mesh instances.", Tags = new[] { "enabled" })]
		public bool Enabled = true;

		[Input]
		[PcgMemberInfo("Regions to turn into a mesh.", Tags = new[] { "region", "source" })]
		public RegionSet Region;

		[Input]
		[PcgMemberInfo("Terrain the mesh is draped over; empty keeps it flat.", Tags = new[] { "terrain" })]
		public TerrainData Terrain;

		[Input]
		[PcgMemberInfo("World-space offset applied to the regions.", Tags = new[] { "offset", "position" })]
		public Vector3 Offset;

		[Input]
		[PcgMemberInfo("Maximum height error driving quadtree subdivision.", Tags = new[] { "error", "height" })]
		public float MaxHeightError = 0.25f;

		[Input]
		[PcgMemberInfo("Smallest allowed mesh cell size.", Tags = new[] { "cell", "min" })]
		public float MinCellSize = 1f;

		[Input]
		[PcgMemberInfo("Largest allowed mesh cell size; not greater than zero disables terrain draping.", Tags = new[] { "cell", "max" })]
		public float MaxCellSize = 16f;

		[Input]
		[PcgMemberInfo("Maximum quadtree subdivision depth.", Tags = new[] { "depth", "max" })]
		public int MaxDepth = 6;

		[Input]
		[PcgMemberInfo("Vertical offset above the terrain surface.", Tags = new[] { "height", "offset" })]
		public float HeightOffset = 0.1f;

		[Input]
		[PcgMemberInfo("UV scale applied to the mesh.", Tags = new[] { "uv", "scale" })]
		public float UvScale = 0.1f;

		[Input]
		[PcgMemberInfo("Name of the created mesh objects.", Tags = new[] { "name" })]
		public string Name = "Road";

		[Input]
		[PcgMemberInfo("Material assigned to the mesh.", Tags = new[] { "material" })]
		public Material Material;

		[PcgMemberInfo("Whether a MeshCollider is created for the generated surface.", Tags = new[] { "collider" })]
		public bool Collider;

		[Output]
		[PcgMemberInfo("Generated mesh instance data.", Tags = new[] { "mesh", "instances", "results" })]
		public List<MeshInstanceData> Results => default;
	}
}
