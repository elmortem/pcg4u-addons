using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.Splines;
using PCG.Splines;

namespace PCG.Polygons
{
	[PcgNodeInfo("Converts closed splines into polygon regions.",
		DisplayName = "Spline To Region",
		Category = "Polygons",
		Tags = new[] { "region", "spline", "convert" })]
	public class SplineToRegionNode : PcgPreviewNode
	{
		[Input(Connection = PcgConnectionType.Override)]
		[PcgMemberInfo("Closed splines to convert into regions.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Maximum edge length used when resampling the contour.", Tags = new[] { "segment", "resample" })]
		public float MaxSegmentLength = 1f;

		[Output]
		[PcgMemberInfo("The generated region set.", Tags = new[] { "region", "results" })]
		public RegionSet Result => default;
	}
}
