#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines.Utilities
{
	public static class SplinesUtility
	{
		public static bool IsInsideSpline(this Spline spline, Vector3 point)
		{
			if (spline == null || spline.Count < 3)
				return false;

			SplinesCache.GetCachedPositions(spline, 16, out var positions);
			if (positions == null || positions.Length < 3)
				return false;

			float px = point.x;
			float pz = point.z;
			bool inside = false;
			int n = positions.Length;
			for (int i = 0, j = n - 1; i < n; j = i++)
			{
				float zi = positions[i].z;
				float zj = positions[j].z;
				if ((zi > pz) != (zj > pz))
				{
					float x = positions[j].x + (pz - zj) * (positions[i].x - positions[j].x) / (zi - zj);
					if (x > px)
						inside = !inside;
				}
			}

			return inside;
		}
	}
}
#endif