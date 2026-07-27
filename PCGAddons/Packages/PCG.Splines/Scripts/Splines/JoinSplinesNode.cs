using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Joins open splines whose ends are close together.",
		DisplayName = "Join Splines",
		Category = "Splines",
		Tags = new[] { "spline", "join", "merge" })]
	public class JoinSplinesNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Splines to join by their endpoints.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Maximum distance between endpoints to join.", Tags = new[] { "threshold", "distance" })]
		public float Threshold = 0.5f;

		[Output]
		[PcgMemberInfo("Joined splines.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Results => default;
	}
}
