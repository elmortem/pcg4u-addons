using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;
using PCG.Splines;

namespace PCG.TransformPoints
{
	[PcgNodeInfo("Changes point density by distance to the nearest spline.",
		DisplayName = "Density By Distance To Splines",
		Category = "Transform Points/Spline",
		Tags = new[] { "points", "spline", "density", "distance" })]
	public class DensityByDistanceToSplinesNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Points whose density is changed.", Tags = new[] { "points", "source" })]
		public PcgPointCloud Points = new();

		[Input]
		[PcgMemberInfo("Splines to measure the distance to.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Distance at which the density curve ends.", Tags = new[] { "radius", "distance" })]
		public float Radius = 5f;

		[PcgMemberInfo("Density factor mapped over the normalized distance.", Tags = new[] { "curve", "falloff" })]
		public AnimationCurve Curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

		[NodeEnum]
		[PcgMemberInfo("How the curve value is applied to the density.", Tags = new[] { "mode" })]
		public ChangeDensityMode Mode = ChangeDensityMode.Mult;

		[Output]
		[PcgMemberInfo("Points with modified density.", Tags = new[] { "points", "results" })]
		public PcgPointCloud Results => default;
	}
}
