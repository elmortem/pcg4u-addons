using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Points;
using UnityEngine;

namespace PCG.Mazes
{
	[PcgNodeInfo("Builds a rectangular grid graph.",
		DisplayName = "Grid Graph",
		Category = "Mazes",
		Tags = new[] { "graph", "grid", "maze" })]
	public class GridGraphNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Grid size in cells along X and Y.", Tags = new[] { "size", "grid" })]
		public Vector2Int Size = new(10, 10);

		[Input]
		[PcgMemberInfo("World size of a single grid cell.", Tags = new[] { "cell", "size" })]
		public Vector2 CellSize = new Vector2(1f, 1f);

		[Output]
		[PcgMemberInfo("The generated grid graph.", Tags = new[] { "graph", "results" })]
		public Graph Result => default;

		[Output]
		[PcgMemberInfo("Points at the center of each cell.", Tags = new[] { "points", "centers" })]
		public PcgPointCloud CenterPoints => default;
	}
}
