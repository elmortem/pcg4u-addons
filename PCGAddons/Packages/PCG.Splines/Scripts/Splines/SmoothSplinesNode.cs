using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class SmoothSplinesNode : PcgPreviewNode
	{
		[Input] public List<Spline> Splines = new();
		[Input] public int Iterations = 1;
		[Input] public float Strength = 0.5f;

		[Output] public List<Spline> Results => default;
	}
}
