using System;
using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine;

namespace PCG.Sweep
{
	[Serializable]
	[PcgNodeInfo("Builds a 2D sweep profile.",
		DisplayName = "Profile",
		Category = "Sweep",
		Tags = new[] { "sweep", "profile", "section" })]
	public class ProfileNode : PcgNode
	{
		[NodeEnum]
		[PcgMemberInfo("Shape of the profile cross-section.", Tags = new[] { "shape" })]
		public ProfileShape Shape = ProfileShape.Ribbon;

		[Input]
		[PcgMemberInfo("Width of the profile across the sweep direction.", Tags = new[] { "width" })]
		public float Width = 4f;

		[Input]
		[PcgMemberInfo("Height of the profile for Rectangle, HalfPipe and Pipe shapes.", Tags = new[] { "height" })]
		public float Height = 0.5f;

		[Input]
		[PcgMemberInfo("Number of segments around the Pipe cross-section.", Tags = new[] { "pipe", "sides", "segments" })]
		public int Sides = 16;

		[PcgMemberInfo("Points of the Custom profile in profile space.", Tags = new[] { "custom", "points" })]
		public List<Vector2> CustomPoints = new();

		[PcgMemberInfo("Whether the Custom profile is a closed contour.", Tags = new[] { "custom", "closed" })]
		public bool CustomClosed;

		[Output]
		[PcgMemberInfo("Generated sweep profile.", Tags = new[] { "profile", "output" })]
		public SweepProfile Profile => default;
	}
}
