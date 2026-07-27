using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Points;
using PCG.GraphModel;

namespace PCG.Splines
{
	[Serializable]
	[PcgNodeInfo("Builds random splines connecting pairs of points.",
		DisplayName = "Random Spline",
		Category = "Splines",
		Tags = new[] { "spline", "random", "points", "create" })]
	public class RandomSplineNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Random splines generated from the points.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Results => default;

		[Input]
		[PcgMemberInfo("Points used as spline endpoints.", Tags = new[] { "points", "source" })]
		public PcgPointCloud Points = new();

		[Input]
		[PcgMemberInfo("Up axis used to orient the splines.", Tags = new[] { "up", "axis" })]
		public Vector3 Up = new(0f, 1f, 0f);

		[Input]
		[PcgMemberInfo("Number of segments per spline.", Tags = new[] { "segments", "count" })]
		public int Segments = 10;

		[Input]
		[PcgMemberInfo("Random height range of the spline arcs.", Tags = new[] { "height", "range" })]
		public Vector2 Height = new Vector2(3f, 5f);

		[Input]
		[PcgMemberInfo("Random seed for point pairing and height.", Tags = new[] { "seed", "random" })]
		public int Seed = 0;
	}
}
