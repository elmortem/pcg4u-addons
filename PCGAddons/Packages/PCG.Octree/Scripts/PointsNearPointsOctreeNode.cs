using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Points;
using Unity.Mathematics;

namespace PCG.Octree
{
	public class PointsNearPointsOctreeNode : PcgPreviewNode
	{
		[Input] public List<PointData> Points = new();
		[Input] public List<PointData> OtherPoints = new();
		[Input] public float Radius = 1f;
		public float3 WorldCenter = new(500f, 0f, 500f);
		public float WorldSize = 1000f;
		public bool RemoveThemselves;
		public bool UseScale;
		[Output] public List<PointData> Results => default;
		[Output] public List<PointData> NearPoints => default;
	}
}
