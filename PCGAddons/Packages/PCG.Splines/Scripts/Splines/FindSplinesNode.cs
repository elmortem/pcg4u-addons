using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class FindSplinesNode : PcgPreviewNode
	{
		[Output] public List<Spline> Results => default;

		[Input] public string Name = "Spline";

		[Input] public string Tag = "";
	}
}
