using System;
using PCG.Values;
using UnityEngine;

namespace PCG.Polygons
{
	[Serializable]
	[PcgValueMenuPath("Polygons/Region Set")]
	public sealed class RegionSetValue : PcgValue
	{
		public override Type ValueType => typeof(RegionSet);

		public override object GetValue(Transform transform)
		{
			return new RegionSet();
		}

		public override int GetContentHash()
		{
			return 0;
		}
	}
}
