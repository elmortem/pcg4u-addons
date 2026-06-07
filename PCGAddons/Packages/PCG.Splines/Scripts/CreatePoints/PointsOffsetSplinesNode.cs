using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;
using PCG.Splines;

namespace PCG.CreatePoints
{
	public class PointsOffsetSplinesNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;
		[Output] public List<PointData> CornerPoints => default;

		[Input] public List<Spline> Splines = new();
		[Input] public float Offset = 5f;
		[Input] public float Distance = 2f;
		[Input] public int Count = 10;
		[NodeEnum]
		public SplineSpacingMode Spacing = SplineSpacingMode.Distance;
		public bool BothSides = true;
		public bool UpNormal;
		public bool NoRotation;
	}
}
