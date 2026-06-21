#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons.Utilities
{
	public static class RegionGizmoUtility
	{
		public static void Draw(RegionSet set, Color outerColor, Color holeColor)
		{
			if (set == null || set.Regions.Count <= 0)
				return;

			for (int i = 0; i < set.Regions.Count; i++)
			{
				var region = set.Regions[i];

				Gizmos.color = outerColor;
				DrawRing(region.Outer, set.PlaneY);

				Gizmos.color = holeColor;
				for (int h = 0; h < region.Holes.Count; h++)
				{
					DrawRing(region.Holes[h], set.PlaneY);
				}
			}
		}

		private static void DrawRing(float2[] ring, float planeY)
		{
			if (ring == null || ring.Length < 2)
				return;

			int j = ring.Length - 1;
			for (int i = 0; i < ring.Length; i++)
			{
				var from = new Vector3(ring[j].x, planeY, ring[j].y);
				var to = new Vector3(ring[i].x, planeY, ring[i].y);
				Gizmos.DrawLine(from, to);
				j = i;
			}
		}
	}
}
#endif
