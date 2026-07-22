using UnityEngine;

namespace PCG.Sweep
{
	public sealed class SweepNetworkSnapshot
	{
		public SweepSnapshot Pieces;
		public SweepMeshData[] PieceMeshes;
		public SweepNetworkJunction[] Junctions;
		public Vector3[][] PieceStartRings;
		public Vector3[][] PieceEndRings;
		public float Step;
		public float MaxAngleRad;
		public float UvScale;
		public float HeightOffset;
		public bool Collider;
		public bool CapEnds;
		public string Name;
		public Material JunctionMaterial;
	}
}
