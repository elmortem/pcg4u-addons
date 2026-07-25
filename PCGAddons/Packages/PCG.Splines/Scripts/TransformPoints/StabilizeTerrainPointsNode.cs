using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.TransformPoints
{
	[PcgNodeInfo("Filters unsuitable slopes, reduces terrain-driven point tilt, and sinks corrected roots into the surface.",
		DisplayName = "Stabilize Terrain Points",
		Category = "Transform Points/Terrain",
		Tags = new[] { "points", "terrain", "normal", "tilt", "roots" })]
	public sealed class StabilizeTerrainPointsNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Terrain-projected points to stabilize.", Tags = new[] { "points", "source" })]
		public List<PointData> Points = new();

		[Input]
		[PcgMemberInfo("Maximum terrain slope where points are retained.", Tags = new[] { "normal", "slope", "angle", "filter" })]
		public float MaxTerrainSlopeDegrees = 36f;

		[Input]
		[PcgMemberInfo("Divisor applied to retained terrain tilt.", Tags = new[] { "normal", "tilt", "angle", "scale" })]
		public float TiltReductionFactor = 3f;

		[Input]
		[PcgMemberInfo("Approximate unscaled root radius used for geometric sink compensation.", Tags = new[] { "roots", "radius", "sink" })]
		public float RootRadius = 0.65f;

		[Input]
		[PcgMemberInfo("Maximum world-space downward compensation.", Tags = new[] { "roots", "sink", "offset" })]
		public float MaxSink = 0.5f;

		[Output]
		[PcgMemberInfo("Slope-filtered points with reduced tilt and compensated root positions.", Tags = new[] { "points", "results" })]
		public List<PointData> Results => default;
	}
}
