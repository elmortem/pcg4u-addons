using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Instances;
using UnityEngine;

namespace PCG.Polygons.City
{
	public sealed class RegionToMeshNode : PcgPreviewNode
	{
		public bool Enabled = true;

		[Input]
		public RegionSet Region;

		[Input]
		public TerrainData Terrain;

		[Input]
		public Vector3 Offset;

		[Input]
		public float MaxHeightError = 0.25f;

		[Input]
		public float MinCellSize = 1f;

		[Input]
		public float MaxCellSize = 16f;

		[Input]
		public int MaxDepth = 6;

		[Input]
		public float HeightOffset = 0.1f;

		[Input]
		public float UvScale = 0.1f;

		[Input]
		public string Name = "Road";

		[Input]
		public Material Material;

		[Output]
		public List<MeshInstanceData> Results => default;
	}
}
