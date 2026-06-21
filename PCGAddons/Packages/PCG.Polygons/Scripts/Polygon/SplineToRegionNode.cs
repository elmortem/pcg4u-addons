using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.Splines;

namespace PCG.Polygons
{
	public class SplineToRegionNode : PcgPreviewNode
	{
		[Input(Connection = PcgConnectionType.Override)]
		public List<Spline> Splines = new();

		[Input]
		public float MaxSegmentLength = 1f;

		[Output]
		public RegionSet Result => default;
	}
}
