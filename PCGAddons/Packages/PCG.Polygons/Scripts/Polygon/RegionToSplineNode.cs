using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.Splines;

namespace PCG.Polygons
{
	public class RegionToSplineNode : PcgPreviewNode
	{
		[Input]
		public RegionSet Region;

		[Output]
		public List<Spline> Splines => default;
	}
}
