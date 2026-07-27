using System;
using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;
using PCG.Splines;

namespace PCG.CreatePoints
{
	[Serializable]
	[PcgNodeInfo("Creates points along splines by arc-length distance.",
		DisplayName = "Spline Points By Distance",
		Category = "Create Points/Spline",
		Tags = new[] { "points", "spline", "distance", "create" })]
	public class SplinePointsByDistanceNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Points generated along the splines.", Tags = new[] { "points", "results" })]
		public PcgPointCloud Results => default;

		[Input]
		[PcgMemberInfo("Splines to place points along.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines;

		[Input]
		[PcgMemberInfo("Arc-length spacing between points.", Tags = new[] { "distance", "spacing" })]
		public float Distance = 1f;

		[PcgMemberInfo("Whether spacing is evenly distributed to fit the spline length.", Tags = new[] { "distribute", "fit" })]
		public bool Distribute = true;
	}
}
