using System.Collections.Generic;
using PCG.Points;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Splits splines exactly at the given cuts or points.",
		DisplayName = "Split Splines",
		Category = "Splines",
		Tags = new[] { "splines", "split", "cut", "network" })]
	public class SplitSplinesNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("World-space splines to split.", Tags = new[] { "splines", "source" })]
		public PcgSplineSet Splines = new();

		[Input(Connection = PcgConnectionType.Override)]
		[PcgMemberInfo("Exact cut records from Spline Intersection. Each cut applies only to its own spline.", Tags = new[] { "topology", "cuts", "exact" })]
		public SplineNetworkTopology Cuts;

		[Input]
		[PcgMemberInfo("Arbitrary points used as an approximate fuzzy cut source. Cuts every spline closer than Snap Distance.", Tags = new[] { "points", "fuzzy", "cuts" })]
		public PcgPointCloud Points = new();

		[Input]
		[PcgMemberInfo("Maximum distance, in world units, for the fuzzy point cut mode. Unused by the exact cuts input.", Tags = new[] { "snap", "distance", "world" })]
		public float SnapDistance = 0.5f;

		[Output]
		[PcgMemberInfo("Exact spline pieces without resampling or shape change.", Tags = new[] { "splines", "pieces", "results" })]
		public PcgSplineSet Results => default;
	}
}
