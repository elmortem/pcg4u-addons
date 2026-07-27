using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Offsets splines sideways by a distance.",
		DisplayName = "Offset Splines",
		Category = "Splines",
		Tags = new[] { "spline", "offset" })]
	public class OffsetSplinesNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Splines to offset.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Sideways offset distance in world units.", Tags = new[] { "offset", "distance" })]
		public float Offset = 1f;

		[Input]
		[PcgMemberInfo("Up axis defining the offset plane.", Tags = new[] { "up", "axis" })]
		public Vector3 Up = Vector3.up;

		[Output]
		[PcgMemberInfo("Offset splines.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Results => default;
	}
}
