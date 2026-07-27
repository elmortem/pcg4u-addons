using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines.Utilities
{
	public static class SplinesUtility
	{
#if UNITY_EDITOR
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
#endif

		public static int GetContentHash(Spline spline)
		{
			unchecked
			{
				if (spline == null)
					return 0;

				int hash = spline.Count;
				hash = (hash * 397) ^ spline.Closed.GetHashCode();
				for (int k = 0; k < spline.Count; k++)
				{
					var knot = spline[k];
					hash = (hash * 397) ^ knot.Position.GetHashCode();
					hash = (hash * 397) ^ knot.TangentIn.GetHashCode();
					hash = (hash * 397) ^ knot.TangentOut.GetHashCode();
					hash = (hash * 397) ^ knot.Rotation.GetHashCode();
					hash = (hash * 397) ^ (int)spline.GetTangentMode(k);
				}

				if (spline.TryGetFloatData(SplineWidthUtility.DataKey, out var widthData) && widthData != null)
				{
					hash = (hash * 397) ^ (int)widthData.PathIndexUnit;
					hash = (hash * 397) ^ widthData.DefaultValue.GetHashCode();
					foreach (var point in widthData)
					{
						hash = (hash * 397) ^ point.Index.GetHashCode();
						hash = (hash * 397) ^ point.Value.GetHashCode();
					}
				}

				return hash;
			}
		}
	}
}
