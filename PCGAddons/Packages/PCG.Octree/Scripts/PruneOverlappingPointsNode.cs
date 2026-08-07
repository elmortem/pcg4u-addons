using PCG.GraphModel;
using PCG.Points;

namespace PCG.Octree
{
	[PcgNodeInfo("Resolves overlaps between point layers by port priority using an octree.",
		DisplayName = "Prune Overlapping Points",
		Category = "Select Points",
		Tags = new[] { "points", "octree", "prune", "overlap", "select" })]
	public class PruneOverlappingPointsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Highest priority layer; never pruned by other layers.", Tags = new[] { "points", "layer" })]
		public PcgPointCloud In0 = new();

		[Input]
		[PcgMemberInfo("Second priority layer.", Tags = new[] { "points", "layer" })]
		public PcgPointCloud In1 = new();

		[Input]
		[PcgMemberInfo("Third priority layer.", Tags = new[] { "points", "layer" })]
		public PcgPointCloud In2 = new();

		[Input]
		[PcgMemberInfo("Lowest priority layer.", Tags = new[] { "points", "layer" })]
		public PcgPointCloud In3 = new();

		[PcgMemberInfo("Base footprint radius of layer 0 points.", Tags = new[] { "radius" })]
		public float Radius0 = 1f;

		[PcgMemberInfo("Base footprint radius of layer 1 points.", Tags = new[] { "radius" })]
		public float Radius1 = 1f;

		[PcgMemberInfo("Base footprint radius of layer 2 points.", Tags = new[] { "radius" })]
		public float Radius2 = 1f;

		[PcgMemberInfo("Base footprint radius of layer 3 points.", Tags = new[] { "radius" })]
		public float Radius3 = 1f;

		[PcgMemberInfo("Whether layer 0 points prune each other.", Tags = new[] { "self", "prune" })]
		public bool SelfPrune0;

		[PcgMemberInfo("Whether layer 1 points prune each other.", Tags = new[] { "self", "prune" })]
		public bool SelfPrune1 = true;

		[PcgMemberInfo("Whether layer 2 points prune each other.", Tags = new[] { "self", "prune" })]
		public bool SelfPrune2 = true;

		[PcgMemberInfo("Whether layer 3 points prune each other.", Tags = new[] { "self", "prune" })]
		public bool SelfPrune3 = true;

		[PcgMemberInfo("Two points conflict when their XZ distance is below Overlap times the sum of their radii.", Tags = new[] { "overlap", "factor" })]
		public float Overlap = 0.9f;

		[Output]
		[PcgMemberInfo("Accepted points of layer 0.", Tags = new[] { "points", "results" })]
		public PcgPointCloud Out0 => default;

		[Output]
		[PcgMemberInfo("Accepted points of layer 1.", Tags = new[] { "points", "results" })]
		public PcgPointCloud Out1 => default;

		[Output]
		[PcgMemberInfo("Accepted points of layer 2.", Tags = new[] { "points", "results" })]
		public PcgPointCloud Out2 => default;

		[Output]
		[PcgMemberInfo("Accepted points of layer 3.", Tags = new[] { "points", "results" })]
		public PcgPointCloud Out3 => default;
	}
}
