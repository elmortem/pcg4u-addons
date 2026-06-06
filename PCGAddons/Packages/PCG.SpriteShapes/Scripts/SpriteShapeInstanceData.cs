using System;
using UnityEngine.U2D;
using PCG.Instances;
using Spline = UnityEngine.Splines.Spline;

namespace PCG.SpriteShapes
{
	/// <summary>
	/// Instance data for a SpriteShape renderer. Contains spline path, profile, and height.
	/// </summary>
	[Serializable]
	public class SpriteShapeInstanceData : InstanceData
	{
		/// <summary>
		/// Name for the generated sprite shape GameObject.
		/// </summary>
		public string Name = "SpriteShape";
		/// <summary>
		/// Spline defining the shape path.
		/// </summary>
		public Spline Spline;
		/// <summary>
		/// SpriteShape profile asset.
		/// </summary>
		public SpriteShape SpriteShape;
		/// <summary>
		/// Height/scale factor for the sprite shape.
		/// </summary>
		public float Height;
	}
}