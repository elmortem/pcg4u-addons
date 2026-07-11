using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using UnityEngine.Splines;

namespace PCG.Mazes.Splines
{
	[PcgNodeInfo("Converts graph edges into bezier splines.",
		DisplayName = "Graph To Spline",
		Category = "Mazes",
		Tags = new[] { "graph", "spline", "convert" })]
	public class GraphToSplineNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Graph whose edges become splines.", Tags = new[] { "graph", "source" })]
		public Graph Graph = new();

		[PcgMemberInfo("Whether to auto-smooth the spline tangents.", Tags = new[] { "smooth", "tangents" })]
		public bool AutoSmooth = true;

		[Output]
		[PcgMemberInfo("Splines generated from the graph edges.", Tags = new[] { "spline", "results" })]
		public List<Spline> Splines => default;
	}
}
