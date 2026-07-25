using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using UnityEngine.Splines;

namespace PCG.Polygons.Convert
{
	[PcgNodeInfo("Converts spline paths into a planar graph.",
		DisplayName = "Splines To Graph",
		Category = "Polygons/Convert",
		Tags = new[] { "spline", "graph", "convert", "planar" })]
	public sealed class SplinesToGraphNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Splines to convert into graph edges.", Tags = new[] { "spline", "source" })]
		public List<Spline> Splines = new();

		[Input]
		[PcgMemberInfo("Maximum sampled edge length along curved splines.", Tags = new[] { "segment", "length", "accuracy" })]
		public float MaxSegmentLength = 2f;

		[Input]
		[PcgMemberInfo("Distance used to merge coincident graph vertices.", Tags = new[] { "merge", "distance", "vertex" })]
		public float MergeDistance = 0.05f;

		[Output]
		[PcgMemberInfo("Generated planar graph.", Tags = new[] { "graph", "results" })]
		public Graph Graph => default;
	}
}
