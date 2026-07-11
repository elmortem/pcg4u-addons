using PCG.GraphModel;
using UnityEngine;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Assigns road width to block edges by their cut depth class.",
		DisplayName = "Assign Road Class By Depth",
		Category = "Polygons/City",
		Tags = new[] { "region", "city", "road", "width" })]
	public sealed class AssignRoadClassByDepthNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Blocks whose edges receive road widths.", Tags = new[] { "region", "blocks", "source" })]
		public RegionSet Blocks;

		[PcgMemberInfo("Road width factor mapped over the normalized cut depth.", Tags = new[] { "width", "curve" })]
		public AnimationCurve WidthByDepth = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);

		[Input]
		[PcgMemberInfo("Maximum road width in world units.", Tags = new[] { "width", "max" })]
		public float MaxWidth = 8f;

		[Input]
		[PcgMemberInfo("Lowest cut-depth class that receives a road width.", Tags = new[] { "depth", "min" })]
		public int MinDepth = 1;

		[Input]
		[PcgMemberInfo("Highest cut-depth class that receives a road width.", Tags = new[] { "depth", "max" })]
		public int MaxDepth = 6;

		[Output]
		[PcgMemberInfo("Blocks with road widths written to their edges.", Tags = new[] { "region", "results" })]
		public RegionSet Result => default;
	}
}
