using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.Points;
using PCG.GraphModel;

namespace PCG.SelectPoints
{
	[PcgNodeInfo("Selects points close to splines within a distance.",
		DisplayName = "Points Near Splines",
		Category = "Select Points/Spline",
		Tags = new[] { "points", "spline", "select", "near" })]
	public class PointsNearSplinesNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Points far from the splines.", Tags = new[] { "points", "far", "results" })]
		public List<PointData> Results => default;

		[Output]
		[PcgMemberInfo("Points near the splines.", Tags = new[] { "points", "near" })]
		public List<PointData> NearPoints => default;

		[Input]
		[PcgMemberInfo("Points to test against the splines.", Tags = new[] { "points", "source" })]
		public List<PointData> Points = new();

		[Input]
		[PcgMemberInfo("Splines to measure the distance to.", Tags = new[] { "spline", "source" })]
		public List<Spline> Splines;

		[Input]
		[PcgMemberInfo("Maximum distance to count a point as near.", Tags = new[] { "distance", "radius" })]
		public float Distance = 1f;

		[PcgMemberInfo("Whether distance is measured in 3D or 2D.", Tags = new[] { "mode", "dimension" })]
		public PointsNearSplinesMode Mode = PointsNearSplinesMode.ThreeD;

		[PcgMemberInfo("Whether the point scale multiplies the distance.", Tags = new[] { "scale" })]
		public bool UseScale;
	}
}
