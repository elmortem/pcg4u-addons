using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Smooths splines with Laplacian relaxation.",
		DisplayName = "Smooth Splines",
		Category = "Splines",
		Tags = new[] { "spline", "smooth", "relax" })]
	public class SmoothSplinesNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Splines to smooth.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Number of smoothing iterations.", Tags = new[] { "iterations", "count" })]
		public int Iterations = 1;

		[Input]
		[PcgMemberInfo("Smoothing strength per iteration.", Tags = new[] { "strength", "amount" })]
		public float Strength = 0.5f;

		[Output]
		[PcgMemberInfo("Smoothed splines.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Results => default;
	}
}
