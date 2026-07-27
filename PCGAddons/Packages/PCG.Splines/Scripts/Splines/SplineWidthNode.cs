using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.Splines;

namespace PCG.Splines
{
	[PcgNodeInfo("Writes an absolute world-space width channel onto splines.",
		DisplayName = "Spline Width",
		Category = "Splines",
		Tags = new[] { "spline", "width", "road", "channel" })]
	public sealed class SplineWidthNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Splines that receive the width channel.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Absolute profile width in world units.", Tags = new[] { "width", "world" })]
		public float Width = 4f;

		[Output]
		[PcgMemberInfo("Copied splines carrying the width channel.", Tags = new[] { "spline", "width", "results" })]
		public PcgSplineSet Results => default;
	}
}
