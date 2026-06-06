using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Points;

namespace PCG.Mazes
{
	public class MazeMstGraphNode : PcgPreviewNode
	{
		[Input] public Graph Graph;
		[Input] public int Seed = -1;

		[Output] public Graph Result => default;
		[Output] public List<PointData> EndPoints => default;
	}
}
