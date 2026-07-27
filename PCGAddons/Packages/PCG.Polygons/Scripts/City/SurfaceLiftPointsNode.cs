using PCG.GraphModel;
using PCG.Points;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Moves projected points vertically so instance roots sit on a raised generated surface.",
		DisplayName = "Surface Lift Points",
		Category = "Polygons/City",
		Tags = new[] { "points", "surface", "lift", "offset", "grass" })]
	public sealed class SurfaceLiftPointsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Points to lift.", Tags = new[] { "points", "source" })]
		public PcgPointCloud Points = new();

		[Input]
		[PcgMemberInfo("World-space vertical offset applied to every point.", Tags = new[] { "height", "lift", "offset" })]
		public float Height = 0.05f;

		[Output]
		[PcgMemberInfo("Lifted points.", Tags = new[] { "points", "results" })]
		public PcgPointCloud Results => default;
	}
}
