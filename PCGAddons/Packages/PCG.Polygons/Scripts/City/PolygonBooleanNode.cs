using PCG.GraphModel;

namespace PCG.Polygons.City
{
	public sealed class PolygonBooleanNode : PcgPreviewNode
	{
		[Input]
		public RegionSet A;

		[Input]
		public RegionSet B;

		public PolygonBooleanMode Mode = PolygonBooleanMode.Difference;

		[Output]
		public RegionSet Result => default;
	}
}
