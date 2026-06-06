using PCG.GraphModel;
using PCG.Mazes.Graphs;

namespace PCG.Mazes
{
	public class GraphMinusGraphNode : PcgPreviewNode
	{
		[Input] public Graph Graph = new();
		[Input] public Graph Minus = new();

		[Output] public Graph Result => default;
	}
}
