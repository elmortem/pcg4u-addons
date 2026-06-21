using System.Collections.Generic;
using PCG.Attributes;

namespace PCG.Polygons
{
	public sealed class RegionSet : IPcgAttributeData
	{
		public List<Polygon2D> Regions = new();
		public float PlaneY;

		public PcgAttributeSet Attributes { get; } = new();

		public int Count => Regions.Count;

		public int AddRegion(Polygon2D polygon)
		{
			Regions.Add(polygon);
			return Attributes.AddRow();
		}

		public RegionSet Clone()
		{
			var copy = new RegionSet();
			copy.PlaneY = PlaneY;
			for (int i = 0; i < Regions.Count; i++)
			{
				copy.Regions.Add(Regions[i].Clone());
			}

			copy.Attributes.Append(Attributes);
			return copy;
		}

		public int GetContentHash()
		{
			unchecked
			{
				int hash = 17;
				hash = (hash * 397) ^ PlaneY.GetHashCode();
				for (int i = 0; i < Regions.Count; i++)
				{
					hash = (hash * 397) ^ Regions[i].GetContentHash();
				}

				hash = (hash * 397) ^ Attributes.GetContentHash();
				return hash;
			}
		}
	}
}
