using PCG.GraphModel;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Performs a boolean operation between two region sets.",
		DisplayName = "Polygon Boolean",
		Category = "Polygons/City",
		Tags = new[] { "region", "boolean", "union", "difference" })]
	public sealed class PolygonBooleanNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("First operand region set.", Tags = new[] { "region", "a" })]
		public RegionSet A;

		[Input]
		[PcgMemberInfo("Second operand region set.", Tags = new[] { "region", "b" })]
		public RegionSet B;

		[PcgMemberInfo("Boolean operation to apply.", Tags = new[] { "mode", "operation" })]
		public PolygonBooleanMode Mode = PolygonBooleanMode.Difference;

		[Output]
		[PcgMemberInfo("The resulting region set.", Tags = new[] { "region", "results" })]
		public RegionSet Result => default;
	}
}
