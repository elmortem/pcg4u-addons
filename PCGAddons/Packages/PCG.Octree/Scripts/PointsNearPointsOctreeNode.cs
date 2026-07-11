using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Points;
using Unity.Mathematics;

namespace PCG.Octree
{
	[PcgNodeInfo("Splits points by whether a neighbor lies within a radius, using an octree.",
		DisplayName = "Points Near Points Octree",
		Category = "Select Points",
		Tags = new[] { "points", "octree", "near", "select" })]
	public class PointsNearPointsOctreeNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Points to test for nearby neighbors.", Tags = new[] { "points", "source" })]
		public List<PointData> Points = new();

		[Input]
		[PcgMemberInfo("Points searched for as neighbors.", Tags = new[] { "points", "others" })]
		public List<PointData> OtherPoints = new();

		[Input]
		[PcgMemberInfo("Neighbor search radius.", Tags = new[] { "radius", "distance" })]
		public float Radius = 1f;

		[PcgMemberInfo("World-space center of the octree bounds.", Tags = new[] { "center", "bounds" })]
		public float3 WorldCenter = new(500f, 0f, 500f);

		[PcgMemberInfo("World-space size of the octree bounds.", Tags = new[] { "size", "bounds" })]
		public float WorldSize = 1000f;

		[PcgMemberInfo("Whether points also search among themselves for duplicates.", Tags = new[] { "self", "duplicates" })]
		public bool RemoveThemselves;

		[PcgMemberInfo("Whether the point scale multiplies the radius.", Tags = new[] { "scale" })]
		public bool UseScale;

		[Output]
		[PcgMemberInfo("Points with no neighbor in the radius.", Tags = new[] { "points", "far", "results" })]
		public List<PointData> Results => default;

		[Output]
		[PcgMemberInfo("Points with a neighbor in the radius.", Tags = new[] { "points", "near" })]
		public List<PointData> NearPoints => default;
	}
}
