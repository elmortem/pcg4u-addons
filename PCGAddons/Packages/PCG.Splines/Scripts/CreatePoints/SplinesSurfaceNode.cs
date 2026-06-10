using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Modes;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.CreatePoints
{
	[Serializable]
	public class SplinesSurfaceNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;

		[Input] public List<Spline> Splines;
		[Input] public Vector3 Offset = Vector3.zero;
		[NodeEnum]
		public GeneratePointMode PointMode;
		[Input] public int Count = 100;
		[Input] public int Seed = 0;
	}
}
