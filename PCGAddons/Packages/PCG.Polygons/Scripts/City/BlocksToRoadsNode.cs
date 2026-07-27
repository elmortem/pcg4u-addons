using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.Splines;
using PCG.Splines;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Builds road strips from block edges that carry a width.",
		DisplayName = "Blocks To Roads",
		Category = "Polygons/City",
		Tags = new[] { "region", "city", "road", "strips" })]
	public sealed class BlocksToRoadsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Blocks whose edges carry road widths.", Tags = new[] { "region", "blocks", "source" })]
		public RegionSet Blocks;

		[PcgMemberInfo("How road strip corners are joined.", Tags = new[] { "join", "corner" })]
		public RoadJoinType Join = RoadJoinType.Round;

		[PcgMemberInfo("How road strip ends are capped.", Tags = new[] { "cap", "end" })]
		public RoadCapType Cap = RoadCapType.Butt;

		[PcgMemberInfo("Miter limit for sharp joined corners.", Tags = new[] { "miter", "limit" })]
		public float MiterLimit = 2f;

		[PcgMemberInfo("Iteratively removes dangling road branches shorter than this length. Zero disables pruning.",
			Tags = new[] { "road", "dead end", "prune", "topology" })]
		public float MinimumDeadEndLength;

		[Output]
		[PcgMemberInfo("The generated road ribbons.", Tags = new[] { "region", "roads", "results" })]
		public RegionSet Roads => default;

		[Output]
		[PcgMemberInfo("Road centerlines carrying their absolute width channel.", Tags = new[] { "spline", "roads", "centerline", "width" })]
		public PcgSplineSet Centerlines => default;
	}
}
