using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Finds splines in the scene by name or tag.",
		DisplayName = "Find Splines",
		Category = "Splines",
		Tags = new[] { "spline", "find", "scene" })]
	public class FindSplinesNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Splines found in the scene.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Results => default;

		[Input]
		[PcgMemberInfo("Object name to search for; empty matches any name.", Tags = new[] { "name", "filter" })]
		public string Name = "Spline";

		[Input]
		[PcgMemberInfo("Object tag to search for; empty matches any tag.", Tags = new[] { "tag", "filter" })]
		public string Tag = "";
	}
}
