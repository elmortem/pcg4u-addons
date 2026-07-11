using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.U2D;
using Spline = UnityEngine.Splines.Spline;

namespace PCG.SpriteShapes
{
	[PcgNodeInfo("Builds SpriteShape instance data from splines.",
		DisplayName = "Sprite Shape Instance",
		Category = "Instances",
		Tags = new[] { "sprite-shape", "instances", "spline" })]
	public class SpriteShapeInstanceNode : PcgNode
	{
		[PcgMemberInfo("Whether the node produces instances.", Tags = new[] { "enabled" })]
		public bool Enabled = true;

		[Input]
		[PcgMemberInfo("Splines that shape the sprite geometry.", Tags = new[] { "spline", "source" })]
		public List<Spline> Splines = new();

		[Input]
		[PcgMemberInfo("Name of the created SpriteShape objects.", Tags = new[] { "name" })]
		public string Name = "SpriteShape";

		[Input]
		[PcgMemberInfo("SpriteShape profile asset to apply.", Tags = new[] { "sprite-shape", "profile" })]
		public SpriteShape SpriteShape;

		[Input]
		[PcgMemberInfo("Height of the SpriteShape fill.", Tags = new[] { "height", "size" })]
		public float Height = 1f;

		[Output]
		[PcgMemberInfo("Generated SpriteShape instance data.", Tags = new[] { "sprite-shape", "instances", "results" })]
		public List<SpriteShapeInstanceData> Results => default;
	}
}
