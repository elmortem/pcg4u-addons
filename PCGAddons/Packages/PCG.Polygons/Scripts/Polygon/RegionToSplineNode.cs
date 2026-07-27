using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.Splines;
using PCG.Splines;

namespace PCG.Polygons
{
	[PcgNodeInfo("Converts regions into closed contour and hole splines.",
		DisplayName = "Region To Spline",
		Category = "Polygons",
		Tags = new[] { "region", "spline", "convert" })]
	public class RegionToSplineNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Region set to convert into splines.", Tags = new[] { "region", "source" })]
		public RegionSet Region;

		[Output]
		[PcgMemberInfo("Contour and hole splines of the regions.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Splines => default;
	}
}
