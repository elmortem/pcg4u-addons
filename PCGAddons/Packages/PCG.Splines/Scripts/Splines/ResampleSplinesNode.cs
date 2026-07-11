using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Resamples splines with a fixed spacing between knots.",
		DisplayName = "Resample Splines",
		Category = "Splines",
		Tags = new[] { "spline", "resample" })]
	public class ResampleSplinesNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Splines to resample.", Tags = new[] { "spline", "source" })]
		public List<Spline> Splines = new();

		[Input]
		[PcgMemberInfo("Arc-length spacing between resampled knots.", Tags = new[] { "step", "spacing" })]
		public float Step = 1f;

		[Output]
		[PcgMemberInfo("Resampled splines.", Tags = new[] { "spline", "results" })]
		public List<Spline> Results => default;
	}
}
