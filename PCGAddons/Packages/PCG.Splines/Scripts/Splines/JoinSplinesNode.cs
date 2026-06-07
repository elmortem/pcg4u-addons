using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class JoinSplinesNode : PcgPreviewNode
	{
		[Input] public List<Spline> Splines = new();
		[Input] public float Threshold = 0.5f;

		[Output] public List<Spline> Results => default;
	}
}
