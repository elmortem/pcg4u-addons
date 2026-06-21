using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Polygons
{
	public static class SplineRegionConvert
	{
		public const float DefaultMaxSegmentLength = 1f;

		public static RegionSet SplinesToRegions(IList<Spline> splines, float maxSegmentLength)
		{
			var set = new RegionSet();
			float ySum = 0f;
			int yCount = 0;

			for (int i = 0; i < splines.Count; i++)
			{
				var spline = splines[i];
				if (!spline.Closed)
				{
					Debug.LogWarning("SplineToRegion: open spline skipped.");
					continue;
				}

				var ring = Resample(spline, maxSegmentLength, out float y);
				if (ring.Length < 3)
					continue;

				var polygon = new Polygon2D { Outer = ring };
				set.AddRegion(polygon);
				ySum += y;
				yCount++;
			}

			set.PlaneY = yCount > 0 ? ySum / yCount : 0f;
			return set;
		}

		public static List<Spline> RegionsToSplines(RegionSet set)
		{
			var result = new List<Spline>();
			for (int i = 0; i < set.Regions.Count; i++)
			{
				var polygon = set.Regions[i];
				result.Add(RingToSpline(polygon.Outer, set.PlaneY));
				for (int h = 0; h < polygon.Holes.Count; h++)
				{
					result.Add(RingToSpline(polygon.Holes[h], set.PlaneY));
				}
			}

			return result;
		}

		private static float2[] Resample(Spline spline, float maxSegmentLength, out float planeY)
		{
			float length = spline.GetLength();
			int count = math.max(3, Mathf.CeilToInt(length / math.max(0.001f, maxSegmentLength)));
			var ring = new float2[count];
			float ySum = 0f;

			for (int i = 0; i < count; i++)
			{
				float t = (float)i / count;
				spline.Evaluate(t, out var position, out _, out _);
				ring[i] = new float2(position.x, position.z);
				ySum += position.y;
			}

			planeY = ySum / count;
			return ring;
		}

		private static Spline RingToSpline(float2[] ring, float planeY)
		{
			var spline = new Spline();
			spline.Closed = true;
			for (int i = 0; i < ring.Length; i++)
			{
				spline.Add(new BezierKnot(new float3(ring[i].x, planeY, ring[i].y)), TangentMode.Linear);
			}

			return spline;
		}
	}
}
