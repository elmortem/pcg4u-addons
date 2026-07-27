using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.Splines;
using PCG.Splines;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Builds a unified polygon corridor around open and closed splines.",
		DisplayName = "Spline Corridor Region",
		Category = "Polygons/City",
		Tags = new[] { "spline", "road", "corridor", "region" })]
	public sealed class SplineCorridorRegionNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Centerline splines used to build the corridor.", Tags = new[] { "spline", "road", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Full corridor width in world units.", Tags = new[] { "width", "road" })]
		public float Width = 6f;

		[Input]
		[PcgMemberInfo("Maximum distance between contour samples.", Tags = new[] { "step", "resolution" })]
		public float MaxSegmentLength = 1f;

		[NodeEnum]
		[PcgMemberInfo("Corner join style.", Tags = new[] { "join", "corner" })]
		public RoadJoinType Join = RoadJoinType.Round;

		[NodeEnum]
		[PcgMemberInfo("Open spline end-cap style.", Tags = new[] { "cap", "end" })]
		public RoadCapType Cap = RoadCapType.Round;

		[Output]
		[PcgMemberInfo("Unified corridor regions.", Tags = new[] { "region", "road", "results" })]
		public RegionSet Result => default;
	}
}
