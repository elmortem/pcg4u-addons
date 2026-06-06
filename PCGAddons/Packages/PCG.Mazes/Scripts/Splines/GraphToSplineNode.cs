using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using UnityEngine.Splines;

namespace PCG.Mazes.Splines
{
	public class GraphToSplineNode : PcgPreviewNode
	{
		[Input] public Graph Graph = new();
		public bool AutoSmooth = true;

		[Output] public List<Spline> Splines => default;
	}
}
