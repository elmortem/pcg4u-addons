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
	[PcgNodeInfo("Scatters points on the surface or volume of closed splines.",
		DisplayName = "Splines Surface",
		Category = "Create Points/Spline",
		Tags = new[] { "points", "spline", "surface", "scatter", "create" })]
	public class SplinesSurfaceNode : PcgPreviewNode
	{
		[Output]
		[PcgMemberInfo("Points generated inside the closed splines.", Tags = new[] { "points", "results" })]
		public List<PointData> Results => default;

		[Input]
		[PcgMemberInfo("Closed splines defining the fill area.", Tags = new[] { "spline", "region" })]
		public List<Spline> Splines;

		[Input]
		[PcgMemberInfo("World-space offset applied to the fill area.", Tags = new[] { "offset", "position" })]
		public Vector3 Offset = Vector3.zero;

		[NodeEnum]
		[PcgMemberInfo("Point generation mode (surface or volume, regular or random).", Tags = new[] { "mode" })]
		public GeneratePointMode PointMode;

		[Input]
		[PcgMemberInfo("Number of points to scatter.", Tags = new[] { "count", "amount" })]
		public int Count = 100;

		[Input]
		[PcgMemberInfo("Random seed for scattering.", Tags = new[] { "seed", "random" })]
		public int Seed = 0;
	}
}
