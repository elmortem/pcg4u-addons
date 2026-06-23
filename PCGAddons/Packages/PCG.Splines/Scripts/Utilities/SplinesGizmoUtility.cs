#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines.Utilities
{
	public static class SplinesGizmoUtility
	{
		public static void DrawGizmos(List<Spline> splines, Transform transform)
		{
			if (splines == null || splines.Count <= 0)
				return;

			Gizmos.matrix = transform.localToWorldMatrix;
			foreach (var spline in splines)
			{
				if(spline == null || spline.Count < 2)
					continue;

				Vector3[] positions;
				SplinesCache.GetCachedPositions(spline, 16, out positions);

#if UNITY_2023_1_OR_NEWER
                Gizmos.DrawLineStrip(positions, false);
#else
				for (int i = 1; i < positions.Length; ++i)
					Gizmos.DrawLine(positions[i-1], positions[i]);
#endif
			}
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
#endif