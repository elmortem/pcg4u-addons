using System;
using System.Collections.Generic;
using PCG.Polygons;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepJunctionFootprint
	{
		public SweepJunctionFootprintGap[] Gaps;
		public float2[] Boundary;

		internal static bool TryBuild(SweepNetworkArm[] arms, float2[] portalCw, float2[] portalCcw, float2[][] corridorCw, float2[][] corridorCcw, float step, out SweepJunctionFootprint footprint, out string failure)
		{
			footprint = null;
			failure = null;
			int count = arms.Length;
			if (count < 1)
			{
				failure = "NoArms";
				return false;
			}

			var corridors = new List<Polygon2D>();
			for (int k = 0; k < count; k++)
			{
				float2[] cwPath = corridorCw[k];
				float2[] ccwPath = corridorCcw[k];
				if (cwPath == null || ccwPath == null || cwPath.Length != ccwPath.Length || cwPath.Length < 2)
				{
					failure = "ApproachMissing";
					return false;
				}
				for (int s = 0; s < cwPath.Length - 1; s++)
				{
					var ring = new[]
					{
						cwPath[s],
						ccwPath[s],
						ccwPath[s + 1],
						cwPath[s + 1]
					};
					float area = SignedArea(ring);
					if (math.abs(area) < 1e-8f)
						continue;
					if (area < 0f)
						Array.Reverse(ring);
					corridors.Add(new Polygon2D { Outer = ring });
				}
			}
			if (corridors.Count == 0)
			{
				failure = "ApproachEmpty";
				return false;
			}

			List<Polygon2D> united;
			try
			{
				united = PolygonClipper.Union(corridors, Array.Empty<Polygon2D>());
			}
			catch
			{
				failure = "UnionFailed";
				return false;
			}

			Polygon2D best = null;
			float bestArea = 0f;
			if (united == null)
			{
				failure = "UnionEmpty";
				return false;
			}
			for (int i = 0; i < united.Count; i++)
			{
				var candidate = united[i];
				if (candidate?.Outer == null || candidate.Outer.Length < 3 || candidate.Holes.Count > 0)
					continue;
				float area = math.abs(SignedArea(candidate.Outer));
				if (area > bestArea)
				{
					bestArea = area;
					best = candidate;
				}
			}
			if (best == null)
			{
				int holes = 0;
				for (int i = 0; i < united.Count; i++)
					holes += united[i]?.Holes?.Count ?? 0;
				float outerArea = 0f;
				float holeArea = 0f;
				for (int i = 0; i < united.Count; i++)
				{
					outerArea += united[i]?.Outer == null ? 0f : math.abs(SignedArea(united[i].Outer));
					if (united[i]?.Holes == null)
						continue;
					for (int h = 0; h < united[i].Holes.Count; h++)
						holeArea += math.abs(SignedArea(united[i].Holes[h]));
				}
				failure = "UnionDisconnected-" + united.Count + "-" + holes + "-" + outerArea.ToString("F2") + "-" + holeArea.ToString("F2");
				return false;
			}

			var boundary = new List<float2>(best.Outer);
			RemoveConsecutiveDuplicates(boundary);
			if (boundary.Count < 3)
			{
				failure = "BoundaryEmpty";
				return false;
			}
			if (SignedArea(boundary) < 0f)
				boundary.Reverse();

			float tolerance = math.max(0.003f, math.min(0.01f, math.max(step, 0.05f) * 0.02f));
			for (int k = 0; k < count; k++)
			{
				if (!InsertBoundaryPoint(boundary, portalCw[k], tolerance, out float cwDistance))
				{
					failure = "PortalCwInterior-" + k + "-" + cwDistance.ToString("F3");
					return false;
				}
				if (!InsertBoundaryPoint(boundary, portalCcw[k], tolerance, out float ccwDistance))
				{
					failure = "PortalCcwInterior-" + k + "-" + ccwDistance.ToString("F3");
					return false;
				}
			}

			var cwIndex = new int[count];
			var ccwIndex = new int[count];
			for (int k = 0; k < count; k++)
			{
				cwIndex[k] = FindPoint(boundary, portalCw[k]);
				ccwIndex[k] = FindPoint(boundary, portalCcw[k]);
				if (cwIndex[k] < 0 || ccwIndex[k] < 0 || cwIndex[k] == ccwIndex[k])
				{
					failure = "PortalCollapsed";
					return false;
				}
			}

			var gaps = new SweepJunctionFootprintGap[count];
			for (int k = 0; k < count; k++)
			{
				int kb = (k + 1) % count;
				var forward = BuildPath(boundary.Count, ccwIndex[k], cwIndex[kb], 1);
				var backward = BuildPath(boundary.Count, ccwIndex[k], cwIndex[kb], -1);
				int forwardScore = CountOtherPortals(forward, cwIndex, ccwIndex, ccwIndex[k], cwIndex[kb]);
				int backwardScore = CountOtherPortals(backward, cwIndex, ccwIndex, ccwIndex[k], cwIndex[kb]);
				var path = forwardScore < backwardScore ? forward : (backwardScore < forwardScore ? backward : ShorterPath(boundary, forward, backward));
				var raw = new List<float2>(path.Count);
				for (int i = 0; i < path.Count; i++)
					raw.Add(boundary[path[i]]);
				raw[0] = portalCcw[k];
				raw[raw.Count - 1] = portalCw[kb];
				var smooth = Smooth(raw, 2);
				Resample(smooth, math.max(step, 0.05f), out float2[] plan, out float[] ts);
				if (plan.Length < 2)
				{
					failure = "GapCollapsed";
					return false;
				}
				plan[0] = portalCcw[k];
				plan[plan.Length - 1] = portalCw[kb];
				gaps[k] = new SweepJunctionFootprintGap
				{
					Plan = plan,
					T = ts,
					ReferenceStart = portalCcw[k],
					ReferenceEnd = portalCw[kb]
				};
			}

			footprint = new SweepJunctionFootprint { Gaps = gaps, Boundary = boundary.ToArray() };
			return true;
		}

		private static void RemoveConsecutiveDuplicates(List<float2> points)
		{
			for (int i = points.Count - 1; i >= 0 && points.Count > 1; i--)
			{
				int prev = (i - 1 + points.Count) % points.Count;
				if (math.distancesq(points[i], points[prev]) < 1e-10f)
					points.RemoveAt(i);
			}
		}

		private static bool InsertBoundaryPoint(List<float2> boundary, float2 point, float tolerance, out float distance)
		{
			float toleranceSq = tolerance * tolerance;
			for (int i = 0; i < boundary.Count; i++)
			{
				if (math.distancesq(boundary[i], point) <= toleranceSq)
				{
					distance = math.distance(boundary[i], point);
					boundary[i] = point;
					return true;
				}
			}

			int bestEdge = -1;
			float bestT = 0f;
			float bestDistanceSq = float.MaxValue;
			for (int i = 0; i < boundary.Count; i++)
			{
				float2 a = boundary[i];
				float2 b = boundary[(i + 1) % boundary.Count];
				float2 ab = b - a;
				float len2 = math.dot(ab, ab);
				float t = len2 > 1e-12f ? math.saturate(math.dot(point - a, ab) / len2) : 0f;
				float distanceSq = math.distancesq(point, a + t * ab);
				if (distanceSq < bestDistanceSq)
				{
					bestDistanceSq = distanceSq;
					bestEdge = i;
					bestT = t;
				}
			}
			distance = math.sqrt(bestDistanceSq);
			if (bestEdge < 0 || bestDistanceSq > toleranceSq)
				return false;

			if (bestT <= 1e-4f)
			{
				boundary[bestEdge] = point;
				return true;
			}
			int next = (bestEdge + 1) % boundary.Count;
			if (bestT >= 1f - 1e-4f)
			{
				boundary[next] = point;
				return true;
			}
			boundary.Insert(bestEdge + 1, point);
			return true;
		}

		private static int FindPoint(List<float2> boundary, float2 point)
		{
			for (int i = 0; i < boundary.Count; i++)
			{
				if (math.distancesq(boundary[i], point) < 1e-10f)
					return i;
			}
			return -1;
		}

		private static List<int> BuildPath(int count, int start, int end, int direction)
		{
			var path = new List<int> { start };
			int current = start;
			for (int guard = 0; guard < count; guard++)
			{
				if (current == end)
					break;
				current = (current + direction + count) % count;
				path.Add(current);
				if (current == end)
					break;
			}
			return path;
		}

		private static int CountOtherPortals(List<int> path, int[] cwIndex, int[] ccwIndex, int start, int end)
		{
			int count = 0;
			for (int i = 0; i < path.Count; i++)
			{
				int index = path[i];
				if (index == start || index == end)
					continue;
				for (int p = 0; p < cwIndex.Length; p++)
				{
					if (index == cwIndex[p] || index == ccwIndex[p])
					{
						count++;
						break;
					}
				}
			}
			return count;
		}

		private static List<int> ShorterPath(List<float2> boundary, List<int> a, List<int> b)
		{
			return PathLength(boundary, a) <= PathLength(boundary, b) ? a : b;
		}

		private static float PathLength(List<float2> boundary, List<int> path)
		{
			float length = 0f;
			for (int i = 1; i < path.Count; i++)
				length += math.distance(boundary[path[i - 1]], boundary[path[i]]);
			return length;
		}

		private static List<float2> Smooth(List<float2> source, int iterations)
		{
			var current = new List<float2>(source);
			for (int iteration = 0; iteration < iterations && current.Count > 2; iteration++)
			{
				var next = new List<float2>(current.Count * 2);
				next.Add(current[0]);
				for (int i = 0; i < current.Count - 1; i++)
				{
					float2 a = current[i];
					float2 b = current[i + 1];
					next.Add(math.lerp(a, b, 0.25f));
					next.Add(math.lerp(a, b, 0.75f));
				}
				next.Add(current[current.Count - 1]);
				RemoveConsecutiveDuplicates(next);
				current = next;
			}
			return current;
		}

		private static void Resample(List<float2> source, float spacing, out float2[] points, out float[] ts)
		{
			var cumulative = new float[source.Count];
			for (int i = 1; i < source.Count; i++)
				cumulative[i] = cumulative[i - 1] + math.distance(source[i - 1], source[i]);
			float total = cumulative[cumulative.Length - 1];
			if (total < 1e-6f)
			{
				points = new[] { source[0], source[source.Count - 1] };
				ts = new[] { 0f, 1f };
				return;
			}

			int count = math.max(2, (int)math.ceil(total / spacing) + 1);
			points = new float2[count];
			ts = new float[count];
			int segment = 0;
			for (int i = 0; i < count; i++)
			{
				float t = i / (float)(count - 1);
				float distance = total * t;
				while (segment < source.Count - 2 && cumulative[segment + 1] < distance)
					segment++;
				float segmentLength = cumulative[segment + 1] - cumulative[segment];
				float local = segmentLength > 1e-6f ? (distance - cumulative[segment]) / segmentLength : 0f;
				points[i] = math.lerp(source[segment], source[segment + 1], local);
				ts[i] = t;
			}
		}

		private static float DistanceToSegment(float2 point, float2 a, float2 b)
		{
			float2 ab = b - a;
			float len2 = math.dot(ab, ab);
			float t = len2 > 1e-12f ? math.saturate(math.dot(point - a, ab) / len2) : 0f;
			return math.distance(point, a + t * ab);
		}

		private static float SignedArea(IReadOnlyList<float2> ring)
		{
			float area = 0f;
			for (int i = 0; i < ring.Count; i++)
			{
				float2 a = ring[i];
				float2 b = ring[(i + 1) % ring.Count];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}
	}
}
