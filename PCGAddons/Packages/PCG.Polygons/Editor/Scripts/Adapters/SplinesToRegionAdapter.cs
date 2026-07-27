using PCG.Exec;
using PCG.Splines;

namespace PCG.Polygons
{
	public sealed class SplinesToRegionAdapter : PcgPortAdapter<PcgSplineSet, RegionSet>
	{
		protected override RegionSet Convert(PcgSplineSet value, PcgNodeExecutor consumer)
		{
			var regions = SplineRegionConvert.SplinesToRegions(value.Splines, SplineRegionConvert.DefaultMaxSegmentLength);
			if (regions.Count != value.Count)
				return regions;

			var result = new RegionSet
			{
				PlaneY = regions.PlaneY
			};

			for (int i = 0; i < regions.Regions.Count; i++)
			{
				result.Regions.Add(regions.Regions[i]);
				result.Attributes.AppendRow(value.Attributes, i);
			}

			return result;
		}
	}
}
