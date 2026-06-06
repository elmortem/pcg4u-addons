using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.CreatePoints
{
	public class PointsOffsetSplinesNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;

		[Input] public List<Spline> Splines = new();
		[Input] public float Offset = 5f;
		[Input] public float Distance = 2f;
		public bool BothSides = true;
		public bool UpNormal;
		public bool NoRotation;
	}
}
