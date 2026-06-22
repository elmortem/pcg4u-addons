using PCG.GraphModel;

namespace PCG.Polygons.City
{
	public sealed class PolygonBooleanNode : PcgPreviewNode
	{
		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet A;

		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet B;

		public PolygonBooleanMode Mode = PolygonBooleanMode.Difference;

		[Output]
		public RegionSet Result => default;
	}
}
