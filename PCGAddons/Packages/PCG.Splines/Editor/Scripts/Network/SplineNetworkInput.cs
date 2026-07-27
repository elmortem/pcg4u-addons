using System.Collections.Generic;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public static class SplineNetworkInput
	{
		public static List<Spline> Flatten(PcgSplineSet[] splinesList)
		{
			var result = new List<Spline>();
			if (splinesList == null)
				return result;

			foreach (var splines in splinesList)
			{
				if (splines == null)
					continue;

				foreach (var spline in splines.Splines)
					result.Add(spline);
			}

			return result;
		}
	}
}
