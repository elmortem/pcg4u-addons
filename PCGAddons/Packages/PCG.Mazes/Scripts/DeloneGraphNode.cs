using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Points;

namespace PCG.Mazes
{
	public class DeloneGraphNode : PcgPreviewNode
	{
		[Input] public List<PointData> Points = new();
		[Input] public float MinDistance = 10f;
		[Input] public float MinRatio = 0.3f;

		[Output] public Graph Result => default;
		[Output] public List<PointData> CenterPoints => default;
	}
}
