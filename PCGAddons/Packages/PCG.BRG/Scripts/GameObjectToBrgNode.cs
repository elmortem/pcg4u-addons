using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Instances;

namespace PCG.BRG
{
	public class GameObjectToBrgNode : PcgPreviewNode
	{
		public bool Enabled = true;
		[Input] public List<GameObjectInstanceData> Instances = new();

		[Output] public List<BrgInstanceData> Results => default;
	}
}
