using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Randomly offsets each spline knot within a range.",
		DisplayName = "Change Spline Position",
		Category = "Splines",
		Tags = new[] { "spline", "position", "random", "offset" })]
	public class ChangeSplinePositionNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Splines with displaced knots.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Results => default;

		[Input]
		[PcgMemberInfo("Splines to displace.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Minimum per-axis displacement.", Tags = new[] { "min", "range" })]
		public Vector3 Min = new(-1f, -1f, -1f);

		[Input]
		[PcgMemberInfo("Maximum per-axis displacement.", Tags = new[] { "max", "range" })]
		public Vector3 Max = new(1f, 1f, 1f);

		[Input]
		[PcgMemberInfo("Random seed for the displacement.", Tags = new[] { "seed", "random" })]
		public int Seed = 0;
	}
}
