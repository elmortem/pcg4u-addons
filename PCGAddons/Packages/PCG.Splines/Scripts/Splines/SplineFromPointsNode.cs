using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.Splines
{
	[PcgNodeInfo("Builds a spline that passes through a point cloud.",
		DisplayName = "Spline From Points",
		Category = "Splines",
		Tags = new[] { "spline", "points", "create" })]
	public class SplineFromPointsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Source points the spline passes through.", Tags = new[] { "points", "source" })]
		public List<PointData> Points = new();

		[PcgMemberInfo("Whether the resulting spline is closed into a loop.", Tags = new[] { "closed", "loop" })]
		public bool Closed;

		[Output]
		[PcgMemberInfo("Splines generated from the points.", Tags = new[] { "spline", "results" })]
		public List<Spline> Results => default;
	}
}
