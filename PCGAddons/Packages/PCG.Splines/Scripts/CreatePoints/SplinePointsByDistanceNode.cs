using System;
using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.CreatePoints
{
	[Serializable]
	public class SplinePointsByDistanceNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;

		[Input] public List<Spline> Splines;
		[Input] public float Distance = 1f;
		public bool Distribute = true;
	}
}
