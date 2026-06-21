using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed partial class Polygon2D
	{
		public float2[] Outer;
		public List<float2[]> Holes = new();

		public bool Contains(float2 point)
		{
			if (!ContainsRing(Outer, point))
				return false;

			for (int i = 0; i < Holes.Count; i++)
			{
				if (ContainsRing(Holes[i], point))
					return false;
			}

			return true;
		}

		public void GetBounds(out float2 min, out float2 max)
		{
			min = new float2(float.MaxValue, float.MaxValue);
			max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < Outer.Length; i++)
			{
				min = math.min(min, Outer[i]);
				max = math.max(max, Outer[i]);
			}
		}

		public Polygon2D Clone()
		{
			var copy = new Polygon2D();
			copy.Outer = (float2[])Outer.Clone();
			for (int i = 0; i < Holes.Count; i++)
			{
				copy.Holes.Add((float2[])Holes[i].Clone());
			}

			copy.EdgeAttributes.Append(EdgeAttributes);
			return copy;
		}

		public int GetContentHash()
		{
			unchecked
			{
				int hash = 17;
				hash = HashRing(hash, Outer);
				for (int i = 0; i < Holes.Count; i++)
				{
					hash = HashRing(hash, Holes[i]);
				}

				hash = (hash * 397) ^ EdgeAttributes.GetContentHash();
				return hash;
			}
		}

		private static int HashRing(int hash, float2[] ring)
		{
			hash = (hash * 397) ^ ring.Length;
			for (int i = 0; i < ring.Length; i++)
			{
				hash = (hash * 397) ^ ring[i].GetHashCode();
			}

			return hash;
		}

		private static bool ContainsRing(float2[] ring, float2 point)
		{
			bool inside = false;
			int j = ring.Length - 1;
			for (int i = 0; i < ring.Length; i++)
			{
				var a = ring[i];
				var b = ring[j];
				if (a.y > point.y != b.y > point.y)
				{
					float t = (point.y - a.y) / (b.y - a.y);
					if (point.x < a.x + t * (b.x - a.x))
						inside = !inside;
				}

				j = i;
			}

			return inside;
		}
	}
}
