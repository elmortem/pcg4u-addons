using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class ChangeSplinePositionNode : PcgPreviewNode
	{
		[Output] public List<Spline> Results => default;

		[Input] public List<Spline> Splines = new();

		[Input] public Vector3 Min = new(-1f, -1f, -1f);

		[Input] public Vector3 Max = new(1f, 1f, 1f);

		[Input] public int Seed = 0;
	}
}
