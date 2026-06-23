using System;
using PCG.Instances;
using UnityEngine;

namespace PCG.Polygons
{
	[Serializable]
	public class MeshInstanceData : InstanceData
	{
		public string Name = "Mesh";
		public Material Material;
		public Vector3[] Vertices;
		public Vector2[] Uvs;
		public int[] Triangles;
	}
}
