using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Instances;
using UnityEngine;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Extrudes polygon regions into top and side meshes.",
		DisplayName = "Region Extrude",
		Category = "Polygons/City",
		Tags = new[] { "region", "extrude", "mesh", "sidewalk", "building" })]
	public sealed class RegionExtrudeNode : PcgPreviewNode
	{
		[PcgMemberInfo("Whether the node produces mesh instances.", Tags = new[] { "enabled" })]
		public bool Enabled = true;

		[Input]
		[PcgMemberInfo("Regions to extrude.", Tags = new[] { "region", "source" })]
		public RegionSet Region;

		[Input]
		[PcgMemberInfo("Optional terrain used to fit the extrusion base.", Tags = new[] { "terrain", "height" })]
		public TerrainData Terrain;

		[Input]
		[PcgMemberInfo("World-space origin of the terrain.", Tags = new[] { "terrain", "origin", "offset" })]
		public Vector3 TerrainOffset;

		[NodeEnum]
		[PcgMemberInfo("How the extrusion follows terrain height.", Tags = new[] { "terrain", "fit", "mode" })]
		public RegionExtrudeTerrainMode TerrainMode;

		[Input]
		[PcgMemberInfo("Vertical base offset from the region plane.", Tags = new[] { "base", "offset", "height" })]
		public float BaseOffset;

		[Input]
		[PcgMemberInfo("Extrusion height in world units.", Tags = new[] { "height", "extrude" })]
		public float Height = 0.25f;

		[Input]
		[PcgMemberInfo("UV scale for top and side meshes.", Tags = new[] { "uv", "scale" })]
		public float UvScale = 0.1f;

		[Input]
		[PcgMemberInfo("Name of the created mesh objects.", Tags = new[] { "name" })]
		public string Name = "Region Extrude";

		[Input]
		[PcgMemberInfo("Material assigned to the top surface.", Tags = new[] { "material", "top" })]
		public Material TopMaterial;

		[Input]
		[PcgMemberInfo("Material assigned to the side surfaces; empty reuses the top material.", Tags = new[] { "material", "side" })]
		public Material SideMaterial;

		[PcgMemberInfo("Whether MeshCollider components are created.", Tags = new[] { "collider" })]
		public bool Collider;

		[Output]
		[PcgMemberInfo("Top and side mesh instances.", Tags = new[] { "mesh", "instances", "results" })]
		public List<MeshInstanceData> Results => default;
	}
}
