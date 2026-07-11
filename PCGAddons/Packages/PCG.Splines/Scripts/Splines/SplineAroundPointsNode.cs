using System.Collections.Generic;
using PCG.Points;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	[PcgNodeInfo("Builds a closed spline around each point.",
		DisplayName = "Spline Around Points",
		Category = "Splines",
		Tags = new[] { "spline", "points", "around", "create" })]
	public class SplineAroundPointsNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Closed splines generated around the points.", Tags = new[] { "spline", "results" })]
		public List<Spline> Results => default;

		[Input]
		[PcgMemberInfo("Points to enclose with a spline.", Tags = new[] { "points", "source" })]
		public List<PointData> Points = new();

		[Input]
		[PcgMemberInfo("Random radius range of each enclosing spline.", Tags = new[] { "radius", "size" })]
		public Vector2 Radius = new(0.5f, 1f);

		[Input]
		[PcgMemberInfo("Number of control points per enclosing spline.", Tags = new[] { "count", "points" })]
		public int PointsCount = 4;

		[Input]
		[PcgMemberInfo("Up axis used to orient the enclosing spline.", Tags = new[] { "up", "axis" })]
		public Vector3 Up = new(0f, 1f, 0f);

		[Input]
		[PcgMemberInfo("Random seed for radius and point placement.", Tags = new[] { "seed", "random" })]
		public int Seed = 0;
	}
}
