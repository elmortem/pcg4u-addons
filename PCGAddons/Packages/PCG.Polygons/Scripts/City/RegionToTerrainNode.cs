using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine;

namespace PCG.Polygons.City
{
	public sealed class RegionToTerrainNode : PcgPreviewNode
	{
		public bool Enabled = true;

		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet Region;

		[Input]
		public TerrainData Terrain;

		[Input]
		public Vector3 Offset;

		[Input]
		public float MaxEdgeLength = 2f;

		[Input]
		public int MaxSubdivisions = 4;

		[Input]
		public float HeightOffset = 0.1f;

		[Input]
		public float UvScale = 0.1f;

		[Input]
		public string Name = "Road";

		public Material Material;

		[Output]
		public List<MeshInstanceData> Results => default;
	}
}
