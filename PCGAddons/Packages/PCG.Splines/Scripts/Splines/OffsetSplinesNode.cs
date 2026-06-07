using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class OffsetSplinesNode : PcgPreviewNode
	{
		[Input] public List<Spline> Splines = new();
		[Input] public float Offset = 1f;
		[Input] public Vector3 Up = Vector3.up;

		[Output] public List<Spline> Results => default;
	}
}
