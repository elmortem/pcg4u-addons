using PCG.GraphModel;
using PCG.Mazes.Graphs;

namespace PCG.Mazes
{
	[PcgNodeInfo("Subtracts one graph's edges from another.",
		DisplayName = "Graph Minus Graph",
		Category = "Mazes",
		Tags = new[] { "graph", "subtract", "difference" })]
	public class GraphMinusGraphNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Graph to subtract edges from.", Tags = new[] { "graph", "source" })]
		public Graph Graph = new();

		[Input]
		[PcgMemberInfo("Graph whose edges are removed.", Tags = new[] { "graph", "minus" })]
		public Graph Minus = new();

		[Output]
		[PcgMemberInfo("The resulting graph.", Tags = new[] { "graph", "results" })]
		public Graph Result => default;
	}
}
