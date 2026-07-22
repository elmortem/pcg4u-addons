using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal static class SweepBooleanProfileBuilder
	{
		internal static bool TryBuild(float2[] sourcePoints, float[] sourceUs, int[] sourceSegments, bool closed, float closureThickness, out SweepBooleanProfile profile, out string failure)
		{
			profile = null;
			failure = null;
			if (sourcePoints == null || sourceUs == null || sourceSegments == null || sourceSegments.Length < 2 || sourceSegments.Length % 2 != 0)
			{
				failure = "ProfileEmpty";
				return false;
			}

			var points = new List<float2>();
			var us = new List<float>();
			var keep = new List<bool>();
			var edgeU0 = new List<float>();
			var edgeU1 = new List<float>();
			int edgeCount = sourceSegments.Length / 2;
			for (int edge = 0; edge < edgeCount; edge++)
			{
				int a = sourceSegments[edge * 2];
				int b = sourceSegments[edge * 2 + 1];
				if (a < 0 || b < 0 || a >= sourcePoints.Length || b >= sourcePoints.Length || a >= sourceUs.Length || b >= sourceUs.Length)
				{
					failure = "ProfileIndexInvalid";
					return false;
				}
				if (points.Count == 0)
				{
					points.Add(sourcePoints[a]);
					us.Add(sourceUs[a]);
				}
				else if (math.distancesq(points[points.Count - 1], sourcePoints[a]) > 1e-8f)
				{
					failure = "ProfileDisconnected";
					return false;
				}
				points.Add(sourcePoints[b]);
				us.Add(sourceUs[b]);
				keep.Add(true);
				edgeU0.Add(sourceUs[a]);
				edgeU1.Add(sourceUs[b]);
			}

			RemovePathDuplicates(points, us, keep, edgeU0, edgeU1);
			if (points.Count < 2)
			{
				failure = "ProfileCollapsed";
				return false;
			}

			if (closed)
			{
				if (math.distancesq(points[points.Count - 1], points[0]) > 1e-8f)
				{
					failure = "ClosedProfileOpen";
					return false;
				}
				points.RemoveAt(points.Count - 1);
				us.RemoveAt(us.Count - 1);
				if (points.Count < 3 || keep.Count != points.Count || !IsSimple(points))
				{
					failure = "ClosedProfileInvalid";
					return false;
				}
			}
			else if (!CloseOpen(points, us, keep, edgeU0, edgeU1, closureThickness))
			{
				failure = "OpenProfileClosureFailed";
				return false;
			}

			if (SignedArea(points) > 0f)
				Reverse(points, keep, edgeU0, edgeU1);

			if (math.abs(SignedArea(points)) < 1e-8f || keep.Count != points.Count || !IsSimple(points))
			{
				failure = "ProfilePolygonInvalid";
				return false;
			}

			profile = new SweepBooleanProfile
			{
				Points = points.ToArray(),
				KeepEdges = keep.ToArray(),
				EdgeU0 = edgeU0.ToArray(),
				EdgeU1 = edgeU1.ToArray()
			};
			return true;
		}

		private static bool CloseOpen(List<float2> points, List<float> us, List<bool> keep, List<float> edgeU0, List<float> edgeU1, float closureThickness)
		{
			var chord = new List<float2>(points);
			if (chord.Count >= 3 && math.abs(SignedArea(chord)) > 1e-8f && IsSimple(chord))
			{
				keep.Add(false);
				edgeU0.Add(us[us.Count - 1]);
				edgeU1.Add(us[0]);
				return true;
			}

			float extent = 0f;
			for (int i = 0; i < points.Count; i++)
				extent = math.max(extent, math.length(points[i]));
			float amount = math.max(1e-3f, math.max(closureThickness, extent * 0.01f));
			var directions = new[]
			{
				new float2(0f, -1f),
				new float2(0f, 1f),
				new float2(1f, 0f),
				new float2(-1f, 0f),
				math.normalize(new float2(1f, 1f)),
				math.normalize(new float2(-1f, 1f)),
				math.normalize(new float2(1f, -1f)),
				math.normalize(new float2(-1f, -1f))
			};

			for (int scale = 0; scale < 5; scale++)
			{
				float distance = amount * math.pow(0.5f, scale);
				for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
				{
					float2 offset = directions[directionIndex] * distance;
					var candidate = new List<float2>(points.Count * 2);
					candidate.AddRange(points);
					for (int i = points.Count - 1; i >= 0; i--)
						candidate.Add(points[i] + offset);
					if (math.abs(SignedArea(candidate)) < 1e-8f || !IsSimple(candidate))
						continue;

					int originalCount = points.Count;
					points.Clear();
					points.AddRange(candidate);
					while (keep.Count < points.Count)
					{
						keep.Add(false);
						edgeU0.Add(keep.Count == originalCount ? us[us.Count - 1] : 0f);
						edgeU1.Add(0f);
					}
					return true;
				}
			}
			return false;
		}

		private static void RemovePathDuplicates(List<float2> points, List<float> us, List<bool> keep, List<float> edgeU0, List<float> edgeU1)
		{
			for (int i = points.Count - 1; i > 0; i--)
			{
				if (math.distancesq(points[i], points[i - 1]) > 1e-10f)
					continue;
				points.RemoveAt(i);
				us.RemoveAt(i);
				int edge = i - 1;
				if (edge >= 0 && edge < keep.Count)
				{
					keep.RemoveAt(edge);
					edgeU0.RemoveAt(edge);
					edgeU1.RemoveAt(edge);
				}
			}
		}

		private static void Reverse(List<float2> points, List<bool> keep, List<float> edgeU0, List<float> edgeU1)
		{
			int count = points.Count;
			var oldPoints = points.ToArray();
			var oldKeep = keep.ToArray();
			var oldU0 = edgeU0.ToArray();
			var oldU1 = edgeU1.ToArray();
			for (int i = 0; i < count; i++)
			{
				points[i] = oldPoints[count - 1 - i];
				int oldEdge = (count - 2 - i + count) % count;
				keep[i] = oldKeep[oldEdge];
				edgeU0[i] = oldU1[oldEdge];
				edgeU1[i] = oldU0[oldEdge];
			}
		}

		private static bool IsSimple(IReadOnlyList<float2> points)
		{
			int count = points.Count;
			if (count < 3)
				return false;
			for (int i = 0; i < count; i++)
			{
				float2 a0 = points[i];
				float2 a1 = points[(i + 1) % count];
				if (!math.all(math.isfinite(a0)) || math.distancesq(a0, a1) < 1e-12f)
					return false;
				for (int j = i + 1; j < count; j++)
				{
					if (j == i || j == (i + 1) % count || (j + 1) % count == i)
						continue;
					if (SegmentsIntersect(a0, a1, points[j], points[(j + 1) % count]))
						return false;
				}
			}
			return true;
		}

		private static bool SegmentsIntersect(float2 a, float2 b, float2 c, float2 d)
		{
			float abC = Cross(b - a, c - a);
			float abD = Cross(b - a, d - a);
			float cdA = Cross(d - c, a - c);
			float cdB = Cross(d - c, b - c);
			float epsilon = 1e-7f * math.max(1f, math.max(math.length(b - a), math.length(d - c)));
			if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon)) &&
				((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon)))
				return true;
			return math.abs(abC) <= epsilon && OnSegment(c, a, b, epsilon) ||
				math.abs(abD) <= epsilon && OnSegment(d, a, b, epsilon) ||
				math.abs(cdA) <= epsilon && OnSegment(a, c, d, epsilon) ||
				math.abs(cdB) <= epsilon && OnSegment(b, c, d, epsilon);
		}

		private static bool OnSegment(float2 point, float2 a, float2 b, float epsilon)
		{
			return point.x >= math.min(a.x, b.x) - epsilon && point.x <= math.max(a.x, b.x) + epsilon &&
				point.y >= math.min(a.y, b.y) - epsilon && point.y <= math.max(a.y, b.y) + epsilon;
		}

		private static float SignedArea(IReadOnlyList<float2> points)
		{
			float area = 0f;
			for (int i = 0; i < points.Count; i++)
			{
				float2 a = points[i];
				float2 b = points[(i + 1) % points.Count];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}

		private static float Cross(float2 a, float2 b)
		{
			return a.x * b.y - a.y * b.x;
		}
	}
}
