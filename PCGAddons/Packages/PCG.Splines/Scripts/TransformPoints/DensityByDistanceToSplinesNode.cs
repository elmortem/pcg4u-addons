using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.TransformPoints
{
	public class DensityByDistanceToSplinesNode : PcgPreviewNode
	{
		[Input] public List<PointData> Points = new();
		[Input] public List<Spline> Splines = new();
		[Input] public float Radius = 5f;
		public AnimationCurve Curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
		[NodeEnum]
		public ChangeDensityMode Mode = ChangeDensityMode.Mult;

		[Output] public List<PointData> Results => default;
	}
}
