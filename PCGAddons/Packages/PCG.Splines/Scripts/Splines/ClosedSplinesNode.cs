using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Splits splines into closed and open sets.",
		DisplayName = "Closed Splines",
		Category = "Splines",
		Tags = new[] { "spline", "closed", "open", "filter" })]
	public class ClosedSplinesNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Closed splines from the input.", Tags = new[] { "spline", "closed", "results" })]
		public PcgSplineSet Results => default;

		[Output]
		[PcgMemberInfo("Open splines from the input.", Tags = new[] { "spline", "open" })]
		public PcgSplineSet OpenedSplines => default;

		[Input]
		[PcgMemberInfo("Splines to split by closed state.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();
	}
}
