using PCG.GraphModel;

namespace PCG.Polygons.City
{
	public sealed class InsetRegionNode : PcgPreviewNode
	{
		[Input(Connection = PcgConnectionType.Override)]
		public RegionSet Region;

		[Input]
		public float Delta = -1f;

		[Output]
		public RegionSet Result => default;
	}
}
