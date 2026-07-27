using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;

namespace PCG.Splines
{
	[PcgNodeInfo("Projects spline knots onto a terrain surface.",
		DisplayName = "Spline To Terrain",
		Category = "Splines",
		Tags = new[] { "spline", "terrain", "project" })]
	public class SplineToTerrainNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Splines to project onto the terrain.", Tags = new[] { "spline", "source" })]
		public PcgSplineSet Splines = new();

		[Input]
		[PcgMemberInfo("Terrain the knots are projected onto; empty keeps the splines unchanged.", Tags = new[] { "terrain" })]
		public TerrainData Terrain;

		[FormerlySerializedAs("TerrainOrigin")]
		[Input]
		[PcgMemberInfo("World-space origin of the terrain.", Tags = new[] { "terrain", "origin", "offset" })]
		public Vector3 TerrainOffset;

		[Input]
		[PcgMemberInfo("World-space vertical offset above the terrain surface.", Tags = new[] { "height", "offset" })]
		public float HeightOffset = 0.1f;

		[PcgMemberInfo("Whether knot up vectors follow the terrain normal.", Tags = new[] { "terrain", "normal", "up" })]
		public bool AlignToTerrainNormal;

		[PcgMemberInfo("Whether the spline is resampled before projection.", Tags = new[] { "spline", "resample" })]
		public bool Resample;

		[Input]
		[PcgMemberInfo("Arc-length spacing between resampled knots.", Tags = new[] { "step", "spacing" })]
		public float Step = 2f;

		[Output]
		[PcgMemberInfo("Projected splines.", Tags = new[] { "spline", "results" })]
		public PcgSplineSet Results => default;
	}
}
