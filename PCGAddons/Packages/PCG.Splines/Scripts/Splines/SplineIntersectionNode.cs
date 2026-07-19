using System.Collections.Generic;
using PCG.Points;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Finds junctions of a spline network in the XZ plane.",
		DisplayName = "Spline Intersection",
		Category = "Splines",
		Tags = new[] { "splines", "intersection", "network", "junction", "points" })]
	public class SplineIntersectionNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("World-space splines forming the network.", Tags = new[] { "splines", "source", "network" })]
		public List<Spline> Splines = new();

		[Input]
		[PcgMemberInfo("Maximum geometric error of a junction position, in world units. Drives adaptive curve subdivision.", Tags = new[] { "tolerance", "accuracy", "world" })]
		public float IntersectionTolerance = 0.05f;

		[Input]
		[PcgMemberInfo("Radius, in world units, within which cuts merge into a single junction.", Tags = new[] { "merge", "distance", "world" })]
		public float MergeDistance = 0.5f;

		[Input]
		[PcgMemberInfo("Maximum height difference, in world units, allowed between two branches to form a junction. Zero or less ignores height.", Tags = new[] { "height", "overpass", "world" })]
		public float MaxHeightDifference = 2f;

		[Output]
		[PcgMemberInfo("Topology of the network: junctions with valency and exact incident cuts.", Tags = new[] { "topology", "junction", "cuts" })]
		public SplineNetworkTopology Topology => default;

		[Output]
		[PcgMemberInfo("Junction positions as points for preview and generic point nodes.", Tags = new[] { "points", "junction", "results" })]
		public List<PointData> Results => default;
	}
}
