using UnityEngine;

namespace PCG.Sweep
{
	public struct SweepMeshData
	{
		public Vector3[] Vertices;
		public Vector2[] Uvs;
		public int[] Triangles;
		public Vector3[] StartRing;
		public Vector3[] EndRing;
		public string FailureCode;
	}
}
