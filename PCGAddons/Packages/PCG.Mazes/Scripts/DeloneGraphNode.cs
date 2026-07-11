using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Points;

namespace PCG.Mazes
{
	[PcgNodeInfo("Builds a Delaunay triangulation graph from points.",
		DisplayName = "Delone Graph",
		Category = "Mazes",
		Tags = new[] { "graph", "delaunay", "triangulation" })]
	public class DeloneGraphNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Points to triangulate.", Tags = new[] { "points", "source" })]
		public List<PointData> Points = new();

		[Input]
		[PcgMemberInfo("Minimum distance between graph vertices.", Tags = new[] { "distance", "min" })]
		public float MinDistance = 10f;

		[Input]
		[PcgMemberInfo("Minimum triangle aspect ratio to keep an edge.", Tags = new[] { "ratio", "min" })]
		public float MinRatio = 0.3f;

		[Output]
		[PcgMemberInfo("The generated triangulation graph.", Tags = new[] { "graph", "results" })]
		public Graph Result => default;

		[Output]
		[PcgMemberInfo("Points at the center of each triangle.", Tags = new[] { "points", "centers" })]
		public List<PointData> CenterPoints => default;
	}
}
