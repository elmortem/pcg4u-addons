using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine.U2D;
using Spline = UnityEngine.Splines.Spline;

namespace PCG.SpriteShapes
{
	public class SpriteShapeInstanceNode : PcgNode
	{
		public bool Enabled = true;
		[Input] public List<Spline> Splines = new();
		[Input] public string Name = "SpriteShape";
		[Input] public SpriteShape SpriteShape;
		[Input] public float Height = 1f;

		[Output] public List<SpriteShapeInstanceData> Results => default;
	}
}
