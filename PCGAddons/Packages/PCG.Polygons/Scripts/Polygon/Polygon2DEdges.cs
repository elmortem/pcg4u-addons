using PCG.Attributes;

namespace PCG.Polygons
{
	public sealed partial class Polygon2D
	{
		public PcgAttributeSet EdgeAttributes { get; } = new();

		public int EdgeCount
		{
			get
			{
				int count = Outer.Length;
				for (int i = 0; i < Holes.Count; i++)
				{
					count += Holes[i].Length;
				}

				return count;
			}
		}

		public int HoleEdgeOffset(int hole)
		{
			int offset = Outer.Length;
			for (int i = 0; i < hole; i++)
			{
				offset += Holes[i].Length;
			}

			return offset;
		}

		public bool HasEdgeData()
		{
			return EdgeAttributes.Count == EdgeCount && EdgeCount > 0;
		}

		public T GetEdge<T>(string name, int edgeIndex) where T : struct
		{
			if (!HasEdgeData())
				return default;

			return EdgeAttributes.Get<T>(name, edgeIndex);
		}

		public void SetEdge<T>(string name, int edgeIndex, T value) where T : struct
		{
			if (EdgeAttributes.Count < EdgeCount)
				EdgeAttributes.EnsureCount(EdgeCount);

			EdgeAttributes.Set(name, edgeIndex, value);
		}
	}
}
