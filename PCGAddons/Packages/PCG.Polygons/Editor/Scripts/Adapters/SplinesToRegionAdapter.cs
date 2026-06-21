using System.Collections.Generic;
using PCG.Exec;
using UnityEngine.Splines;

namespace PCG.Polygons
{
	public sealed class SplinesToRegionAdapter : PcgPortAdapter<List<Spline>, RegionSet>
	{
		protected override RegionSet Convert(List<Spline> value, PcgNodeExecutor consumer)
		{
			return SplineRegionConvert.SplinesToRegions(value, SplineRegionConvert.DefaultMaxSegmentLength);
		}
	}
}
