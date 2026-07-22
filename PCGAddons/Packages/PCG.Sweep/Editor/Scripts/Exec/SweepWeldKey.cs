using System;

namespace PCG.Sweep
{
	public readonly struct SweepWeldKey : IEquatable<SweepWeldKey>
	{
		public readonly long Px;
		public readonly long Py;
		public readonly long Pz;
		public readonly long Ux;
		public readonly long Uy;

		public SweepWeldKey(UnityEngine.Vector3 position, UnityEngine.Vector2 uv)
		{
			Px = (long)Math.Round(position.x * 100000d);
			Py = (long)Math.Round(position.y * 100000d);
			Pz = (long)Math.Round(position.z * 100000d);
			Ux = (long)Math.Round(uv.x * 100000d);
			Uy = (long)Math.Round(uv.y * 100000d);
		}

		public bool Equals(SweepWeldKey other)
		{
			return Px == other.Px && Py == other.Py && Pz == other.Pz && Ux == other.Ux && Uy == other.Uy;
		}

		public override bool Equals(object obj)
		{
			return obj is SweepWeldKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = Px.GetHashCode();
				hash = (hash * 397) ^ Py.GetHashCode();
				hash = (hash * 397) ^ Pz.GetHashCode();
				hash = (hash * 397) ^ Ux.GetHashCode();
				hash = (hash * 397) ^ Uy.GetHashCode();
				return hash;
			}
		}
	}
}
