using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Points;
using UnityEngine;

namespace PCG.Mazes
{
	public class GridGraphNode : PcgPreviewNode
	{
		[Input] public Vector2Int Size = new(10, 10);
		[Input] public Vector2 CellSize = new Vector2(1f, 1f);

		[Output] public Graph Result => default;
		[Output] public List<PointData> CenterPoints => default;
	}
}
