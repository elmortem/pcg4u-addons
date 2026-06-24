using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Points;
using PCG.Polygons;

namespace PCG.SelectPoints
{
	public class PointsNearRegionsNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;
		[Output] public List<PointData> NearPoints => default;

		[Input] public List<PointData> Points = new();

		[Input]
		public RegionSet Regions;

		[Input] public float Radius = 1f;

		public bool UseScale;
	}
}
