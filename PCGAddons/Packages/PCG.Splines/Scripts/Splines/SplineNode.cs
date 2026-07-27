using System.Collections.Generic;
using UnityEngine.Splines;
using PCG;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Editable spline authored directly in the graph.",
		DisplayName = "Spline",
		Category = "Splines",
		Tags = new[] { "spline", "edit", "create" })]
	public class SplineNode : PcgPreviewNode
	{
		[HideInNode]
		[PcgMemberInfo("Splines authored with the scene edit tool.", Tags = new[] { "splines", "storage" })]
		public List<Spline> Splines = new();

		[HideInNode]
		[PcgMemberInfo("Knot links between stored splines.", Tags = new[] { "splines", "storage" })]
		public KnotLinkCollection Links = new();

		[Output]
		[PcgMemberInfo("The authored spline.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Results => default;
	}
}
