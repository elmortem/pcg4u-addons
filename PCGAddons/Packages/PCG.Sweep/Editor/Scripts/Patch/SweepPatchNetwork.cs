using UnityEngine;

namespace PCG.Sweep
{
	internal sealed class SweepPatchNetwork
	{
		public SweepMeshData[] Strips;
		public SweepMeshData[] Patches;
		public string[] PatchFailures;
		public Vector3[] HitPoints;
		public Vector3[][] CutChords;
		public bool TerrainOutOfBounds;
		public int ClusterCount;
		public int HitCount;
	}
}
