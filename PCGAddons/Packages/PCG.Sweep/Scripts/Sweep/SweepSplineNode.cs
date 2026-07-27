using System;
using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Instances;
using PCG.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	[Serializable]
	[PcgNodeInfo("Sweeps a 2D profile along splines and builds meshes.",
		DisplayName = "Sweep Spline",
		Category = "Sweep",
		Tags = new[] { "sweep", "mesh", "spline", "road" })]
	public class SweepSplineNode : PcgPreviewNode
	{
		[PcgMemberInfo("Whether the node produces mesh instances.", Tags = new[] { "enabled" })]
		public bool Enabled = true;

		[Input]
		[PcgMemberInfo("Splines the profile is swept along.", Tags = new[] { "splines", "source" })]
		public PcgSplineSet Splines = new();

		[Input(Connection = PcgConnectionType.Override)]
		[PcgMemberInfo("Optional profile override reused from a Profile node.", Tags = new[] { "profile", "override" })]
		public SweepProfile Profile;

		[NodeEnum]
		[PcgMemberInfo("Inline profile shape used when no profile is connected.", Tags = new[] { "shape" })]
		public ProfileShape Shape = ProfileShape.Ribbon;

		[Input]
		[PcgMemberInfo("Inline profile width across the sweep direction.", Tags = new[] { "width" })]
		public float Width = 4f;

		[Input]
		[PcgMemberInfo("Inline profile height for Rectangle, HalfPipe and Pipe shapes.", Tags = new[] { "height" })]
		public float Height = 0.5f;

		[Input]
		[PcgMemberInfo("Number of segments around the inline Pipe cross-section.", Tags = new[] { "pipe", "sides", "segments" })]
		public int Sides = 16;

		[PcgMemberInfo("Inline Custom profile points in profile space.", Tags = new[] { "custom", "points" })]
		public List<Vector2> CustomPoints = new();

		[PcgMemberInfo("Whether the inline Custom profile is a closed contour.", Tags = new[] { "custom", "closed" })]
		public bool CustomClosed;

		[Input]
		[PcgMemberInfo("Minimum length of a sweep segment; rings snap to multiples of this quantum.", Tags = new[] { "step" })]
		public float Step = 1f;

		[Input]
		[PcgMemberInfo("Maximum length of a sweep segment on straight sections.", Tags = new[] { "step", "max" })]
		public float MaxStep = 8f;

		[Input]
		[PcgMemberInfo("Maximum accumulated tangent turn in degrees before the next ring is emitted.", Tags = new[] { "angle", "adaptive" })]
		public float MaxAngle = 5f;

		[PcgMemberInfo("Profile width multiplier by normalized spline length.", Tags = new[] { "width", "curve" })]
		public AnimationCurve WidthByT = AnimationCurve.Constant(0f, 1f, 1f);

		[PcgMemberInfo("Profile height multiplier by normalized spline length.", Tags = new[] { "height", "curve" })]
		public AnimationCurve HeightByT = AnimationCurve.Constant(0f, 1f, 1f);

		[PcgMemberInfo("Profile twist in degrees by normalized spline length.", Tags = new[] { "twist", "curve" })]
		public AnimationCurve TwistByT = AnimationCurve.Constant(0f, 1f, 0f);

		[PcgMemberInfo("Whether to cap the ends of a closed profile on an open spline.", Tags = new[] { "caps" })]
		public bool CapEnds;

		[PcgMemberInfo("Whether splines are split by ribbon width at intersections, leaving free pieces apart.", Tags = new[] { "merge", "intersection", "split" })]
		public bool MergeIntersections;

		[Input]
		[PcgMemberInfo("Vertical tolerance for a ribbon touch; splines farther apart in height than this pass over each other without intersecting (bridges, overpasses).", Tags = new[] { "merge", "thickness", "3d" })]
		public float MergeThickness = 1f;

		[PcgMemberInfo("Whether split pieces, edge intersection points and cut chords are drawn as gizmos.", Tags = new[] { "preview", "gizmos", "debug", "intersection" })]
		public bool ShowIntersections = true;

		[PcgMemberInfo("Debug: draw every Step perpendicular cut, green=clean red=hits another part.", Tags = new[] { "preview", "gizmos", "debug", "cuts" })]
		public bool ShowAllCuts;

		[Input]
		[PcgMemberInfo("Longitudinal UV scale along the sweep.", Tags = new[] { "uv", "scale" })]
		public float UvScale = 0.25f;

		[Input]
		[PcgMemberInfo("Vertical offset added to the mesh height.", Tags = new[] { "height", "offset" })]
		public float HeightOffset = 0.1f;

		[Input]
		[PcgMemberInfo("Name of the created mesh objects.", Tags = new[] { "name" })]
		public string Name = "Sweep";

		[Input]
		[PcgMemberInfo("Material assigned to the mesh.", Tags = new[] { "material" })]
		public Material Material;

		[Input]
		[PcgMemberInfo("Material of junction patches; empty reuses Material.", Tags = new[] { "material", "junction" })]
		public Material JunctionMaterial;

		[PcgMemberInfo("Whether a MeshCollider is added to the mesh objects.", Tags = new[] { "collider" })]
		public bool Collider;

		[Output]
		[PcgMemberInfo("Generated mesh instance data.", Tags = new[] { "mesh", "instances", "results" })]
		public List<MeshInstanceData> Results => default;
	}
}
