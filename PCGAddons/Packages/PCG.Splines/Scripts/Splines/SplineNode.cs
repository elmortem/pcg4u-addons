using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class SplineNode : PcgPreviewNode
	{
		[Output] public List<Spline> Results => default;
	}
}
