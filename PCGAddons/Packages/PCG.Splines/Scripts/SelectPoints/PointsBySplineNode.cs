using System.Collections.Generic;
using PCG.Points;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Splines;

namespace PCG.SelectPoints
{
	[PcgNodeInfo("Selects points that fall inside closed splines.",
		DisplayName = "Points By Spline",
		Category = "Select Points/Spline",
		Tags = new[] { "points", "spline", "select", "inside" })]
	public class PointsBySplineNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Points inside the closed splines.", Tags = new[] { "points", "inside", "results" })]
		public PcgPointCloud Results => default;

		[Output]
		[PcgMemberInfo("Points outside the closed splines.", Tags = new[] { "points", "outside" })]
		public PcgPointCloud Outsides => default;

		[Input]
		[PcgMemberInfo("Points to test against the splines.", Tags = new[] { "points", "source" })]
		public PcgPointCloud Points = new();

		[Input]
		[PcgMemberInfo("Closed splines used as the selection region.", Tags = new[] { "spline", "region" })]
		public PcgSplineSet Splines;
	}
}
