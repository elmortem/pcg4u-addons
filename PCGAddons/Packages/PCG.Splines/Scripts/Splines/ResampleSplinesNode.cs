using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class ResampleSplinesNode : PcgPreviewNode
	{
		[Input] public List<Spline> Splines = new();
		[Input] public float Step = 1f;

		[Output] public List<Spline> Results => default;
	}
}
