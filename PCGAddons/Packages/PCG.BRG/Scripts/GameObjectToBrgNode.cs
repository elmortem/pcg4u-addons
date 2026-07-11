using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Instances;

namespace PCG.BRG
{
	[PcgNodeInfo("Groups game object instances by prefab for BatchRendererGroup rendering.",
		DisplayName = "Game Object To BRG",
		Category = "Instances",
		Tags = new[] { "brg", "instances", "batch", "render" })]
	public class GameObjectToBrgNode : PcgPreviewNode
	{
		[PcgMemberInfo("Whether the node produces instances.", Tags = new[] { "enabled" })]
		public bool Enabled = true;

		[Input]
		[PcgMemberInfo("Game object instances to group by prefab.", Tags = new[] { "instances", "source" })]
		public List<GameObjectInstanceData> Instances = new();

		[Output]
		[PcgMemberInfo("Instances grouped per prefab for BRG.", Tags = new[] { "brg", "instances", "results" })]
		public List<BrgInstanceData> Results => default;
	}
}
