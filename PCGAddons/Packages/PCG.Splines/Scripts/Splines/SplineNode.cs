using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Editable spline authored directly in the graph.",
		DisplayName = "Spline",
		Category = "Splines",
		Tags = new[] { "spline", "edit", "create" })]
	public class SplineNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("The authored spline.", Tags = new[] { "spline", "results" })]
		public List<Spline> Results => default;
	}
}
