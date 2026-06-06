using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class ClosedSplinesNode : PcgPreviewNode
	{
		[Output] public List<Spline> Results => default;
		[Output] public List<Spline> OpenedSplines => default;

		[Input] public List<Spline> Splines = new();
	}
}
