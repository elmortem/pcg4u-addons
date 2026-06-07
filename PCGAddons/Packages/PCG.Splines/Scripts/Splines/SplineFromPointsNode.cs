using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.Splines
{
	public class SplineFromPointsNode : PcgPreviewNode
	{
		[Input] public List<PointData> Points = new();
		public bool Closed;

		[Output] public List<Spline> Results => default;
	}
}
