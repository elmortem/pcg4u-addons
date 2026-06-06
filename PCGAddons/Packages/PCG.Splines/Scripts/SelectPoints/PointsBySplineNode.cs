using System.Collections.Generic;
using PCG.Points;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.SelectPoints
{
	public class PointsBySplineNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;
		[Output] public List<PointData> Outsides => default;

		[Input] public List<PointData> Points = new();
		[Input] public List<Spline> Splines;
	}
}
