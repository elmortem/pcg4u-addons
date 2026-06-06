using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.Points;
using PCG.GraphModel;

namespace PCG.SelectPoints
{
	public class PointsNearSplinesNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;
		[Output] public List<PointData> NearPoints => default;

		[Input] public List<PointData> Points = new();
		[Input] public List<Spline> Splines;
		[Input] public float Distance = 1f;
	}
}
