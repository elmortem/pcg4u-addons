using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	public static class SweepProfileBuilder
	{
		public static SweepProfile Build(ProfileShape shape, float width, float height, int sides, IReadOnlyList<Vector2> customPoints, bool customClosed, Action<string> warn)
		{
			width = math.max(0.01f, width);
			float half = width * 0.5f;

			switch (shape)
			{
				case ProfileShape.Rectangle:
					return BuildRectangle(half, math.max(0.01f, height));
				case ProfileShape.HalfPipe:
					return BuildHalfPipe(half, math.max(0.01f, height));
				case ProfileShape.Pipe:
					return BuildPipe(half, math.max(0.01f, height) * 0.5f, sides);
				case ProfileShape.Custom:
					return BuildCustom(customPoints, customClosed, half, warn);
				default:
					return BuildRibbon(half);
			}
		}

		private static SweepProfile BuildPipe(float halfWidth, float halfHeight, int sides)
		{
			sides = math.max(3, sides);

			var points = new float2[sides + 1];
			var us = new float[sides + 1];
			for (int j = 0; j <= sides; j++)
			{
				float a = 2f * math.PI * j / sides;
				points[j] = new float2(math.cos(a) * halfWidth, -math.sin(a) * halfHeight);
				us[j] = j / (float)sides;
			}

			var segments = new int[sides * 2];
			for (int j = 0; j < sides; j++)
			{
				segments[j * 2] = j;
				segments[j * 2 + 1] = j + 1;
			}

			return new SweepProfile
			{
				Points = points,
				Us = us,
				Segments = segments,
				Closed = true
			};
		}

		private static SweepProfile BuildRibbon(float half)
		{
			return new SweepProfile
			{
				Points = new[] { new float2(-half, 0f), new float2(half, 0f) },
				Us = new[] { 0f, 1f },
				Segments = new[] { 0, 1 },
				Closed = false
			};
		}

		private static SweepProfile BuildRectangle(float half, float height)
		{
			float perimeter = 2f * (half * 2f + height);
			float uBottom = half * 2f / perimeter;
			float uLeft = (half * 2f + height) / perimeter;
			float uTop = (half * 4f + height) / perimeter;

			var points = new[]
			{
				new float2(half, 0f),
				new float2(-half, 0f),
				new float2(-half, 0f),
				new float2(-half, height),
				new float2(-half, height),
				new float2(half, height),
				new float2(half, height),
				new float2(half, 0f)
			};

			var us = new[] { 0f, uBottom, uBottom, uLeft, uLeft, uTop, uTop, 1f };
			var segments = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };

			return new SweepProfile
			{
				Points = points,
				Us = us,
				Segments = segments,
				Closed = true
			};
		}

		private static SweepProfile BuildHalfPipe(float half, float height)
		{
			var points = new float2[9];
			var us = new float[9];
			for (int j = 0; j <= 8; j++)
			{
				float a = math.PI * j / 8f;
				points[j] = new float2(-math.cos(a) * half, -math.sin(a) * height);
				us[j] = j / 8f;
			}

			var segments = new int[16];
			for (int j = 0; j < 8; j++)
			{
				segments[j * 2] = j;
				segments[j * 2 + 1] = j + 1;
			}

			return new SweepProfile
			{
				Points = points,
				Us = us,
				Segments = segments,
				Closed = false
			};
		}

		private static SweepProfile BuildCustom(IReadOnlyList<Vector2> customPoints, bool customClosed, float half, Action<string> warn)
		{
			var cleaned = new List<float2>();
			if (customPoints != null)
			{
				for (int i = 0; i < customPoints.Count; i++)
				{
					float2 p = customPoints[i];
					if (!math.all(math.isfinite(p)))
					{
						warn?.Invoke("Custom profile point is not finite and was dropped.");
						continue;
					}

					if (cleaned.Count > 0 && math.distancesq(cleaned[cleaned.Count - 1], p) < 1e-8f)
						continue;

					cleaned.Add(p);
				}
			}

			if (customClosed && cleaned.Count >= 2 && math.distancesq(cleaned[cleaned.Count - 1], cleaned[0]) < 1e-8f)
				cleaned.RemoveAt(cleaned.Count - 1);

			if (cleaned.Count < 2)
			{
				warn?.Invoke("Custom profile needs at least two valid points; falling back to Ribbon.");
				return BuildRibbon(half);
			}

			if (customClosed)
				return BuildCustomClosed(cleaned, warn);

			return BuildCustomOpen(cleaned);
		}

		private static SweepProfile BuildCustomOpen(List<float2> pts)
		{
			int count = pts.Count;
			var points = new float2[count];
			var us = new float[count];

			float total = 0f;
			for (int i = 0; i < count - 1; i++)
				total += math.distance(pts[i], pts[i + 1]);

			float accumulated = 0f;
			for (int i = 0; i < count; i++)
			{
				points[i] = pts[i];
				us[i] = i == 0 ? 0f : accumulated / total;
				if (i < count - 1)
					accumulated += math.distance(pts[i], pts[i + 1]);
			}
			us[count - 1] = 1f;

			var segments = new int[(count - 1) * 2];
			for (int i = 0; i < count - 1; i++)
			{
				segments[i * 2] = i;
				segments[i * 2 + 1] = i + 1;
			}

			return new SweepProfile
			{
				Points = points,
				Us = us,
				Segments = segments,
				Closed = false
			};
		}

		private static SweepProfile BuildCustomClosed(List<float2> pts, Action<string> warn)
		{
			float area = 0f;
			for (int i = 0; i < pts.Count; i++)
			{
				float2 a = pts[i];
				float2 b = pts[(i + 1) % pts.Count];
				area += a.x * b.y - b.x * a.y;
			}

			if (area > 0f)
			{
				warn?.Invoke("Custom closed profile winding was reversed to keep outward normals.");
				pts.Reverse();
			}

			int count = pts.Count;
			var points = new float2[count + 1];
			var us = new float[count + 1];

			float total = 0f;
			for (int i = 0; i < count; i++)
				total += math.distance(pts[i], pts[(i + 1) % count]);

			float accumulated = 0f;
			for (int i = 0; i < count; i++)
			{
				points[i] = pts[i];
				us[i] = accumulated / total;
				accumulated += math.distance(pts[i], pts[(i + 1) % count]);
			}

			points[count] = pts[0];
			us[count] = 1f;

			var segments = new int[count * 2];
			for (int i = 0; i < count; i++)
			{
				segments[i * 2] = i;
				segments[i * 2 + 1] = i + 1;
			}

			return new SweepProfile
			{
				Points = points,
				Us = us,
				Segments = segments,
				Closed = true
			};
		}
	}
}
