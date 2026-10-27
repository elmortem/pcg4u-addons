using System;
using PCG.Utilities;
using PCG.Values;
using UnityEngine;
using UnityEngine.U2D;

namespace PCG.SpriteShapes
{
	[Serializable]
	[PcgValueMenuPath("SpriteShapes/Sprite Shape")]
	public sealed class SpriteShapeValue : PcgValue
	{
		public SpriteShape Value;

		public override Type ValueType => typeof(SpriteShape);

		public override object GetValue(Transform transform) => Value;

		public override int GetContentHash() => PcgObjectIdentity.Get(Value);
	}
}
