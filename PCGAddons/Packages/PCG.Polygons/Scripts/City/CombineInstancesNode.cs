using System;
using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Instances;

namespace PCG.Polygons.City
{
	[PcgNodeInfo("Combines multiple instance streams into one output.",
		DisplayName = "Combine Instances",
		Category = "Instances",
		Tags = new[] { "instances", "combine", "merge" })]
	public sealed class CombineInstancesNode : PcgNode
	{
		[Output]
		[PcgMemberInfo("Combined instances.", Tags = new[] { "instances", "results" })]
		public List<InstanceData> Results => default;

		[Input]
		[PcgMemberInfo("Instance streams to combine.", Tags = new[] { "instances", "source" })]
		public IReadOnlyList<InstanceData> Instances = Array.Empty<InstanceData>();
	}
}
