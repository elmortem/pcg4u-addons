using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;
using PCG.Splines;

namespace PCG.CreatePoints
{
	[PcgNodeInfo("Creates points along splines offset to the side.",
		DisplayName = "Points Offset Splines",
		Category = "Create Points/Spline",
		Tags = new[] { "points", "spline", "offset", "create" })]
	public class PointsOffsetSplinesNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Points generated along the offset splines.", Tags = new[] { "points", "results" })]
		public List<PointData> Results => default;

		[Output]
		[PcgMemberInfo("Points placed at spline corners.", Tags = new[] { "points", "corners" })]
		public List<PointData> CornerPoints => default;

		[Input]
		[PcgMemberInfo("Splines to place points along.", Tags = new[] { "spline", "source" })]
		public List<Spline> Splines = new();

		[Input]
		[PcgMemberInfo("Sideways offset distance from the spline.", Tags = new[] { "offset", "distance" })]
		public float Offset = 5f;

		[Input]
		[PcgMemberInfo("Spacing distance between points along the spline.", Tags = new[] { "distance", "spacing" })]
		public float Distance = 2f;

		[Input]
		[PcgMemberInfo("Number of points when spacing by count.", Tags = new[] { "count", "amount" })]
		public int Count = 10;

		[NodeEnum]
		[PcgMemberInfo("How points are distributed along the spline.", Tags = new[] { "spacing", "mode" })]
		public SplineSpacingMode Spacing = SplineSpacingMode.Distance;

		[PcgMemberInfo("Whether to place points on both sides of the spline.", Tags = new[] { "sides", "mirror" })]
		public bool BothSides = true;

		[PcgMemberInfo("Whether point normals face the world up axis.", Tags = new[] { "normal", "up" })]
		public bool UpNormal;

		[PcgMemberInfo("Whether points keep no rotation from the spline.", Tags = new[] { "rotation" })]
		public bool NoRotation;
	}
}
