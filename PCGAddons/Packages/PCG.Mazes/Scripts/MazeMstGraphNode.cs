using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Points;

namespace PCG.Mazes
{
	[PcgNodeInfo("Carves a maze from a graph via a minimum spanning tree.",
		DisplayName = "Maze MST Graph",
		Category = "Mazes",
		Tags = new[] { "graph", "maze", "mst" })]
	public class MazeMstGraphNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Source graph to carve the maze from.", Tags = new[] { "graph", "source" })]
		public Graph Graph;

		[Input]
		[PcgMemberInfo("Random seed for the maze carving.", Tags = new[] { "seed", "random" })]
		public int Seed = 0;

		[Output]
		[PcgMemberInfo("The carved maze graph.", Tags = new[] { "graph", "results" })]
		public Graph Result => default;

		[Output]
		[PcgMemberInfo("Points at the maze dead ends.", Tags = new[] { "points", "ends" })]
		public List<PointData> EndPoints => default;
	}
}
