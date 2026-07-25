using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRibbonMeshBuilder
	{
		internal static SweepMeshData Build(SweepRibbonPath path, SweepSnapshot source, int splineIndex, CancellationToken ct, Action reportProgress)
		{
			if (path == null || path.Count < 2 || !(path.Length > 1e-4f))
				return default;

			int count = path.Count;
			float profileHalf = math.max(math.abs(source.ProfilePoints[0].x), math.abs(source.ProfilePoints[1].x));
			int positiveProfile = source.ProfilePoints[0].x >= source.ProfilePoints[1].x ? 0 : 1;
			int negativeProfile = 1 - positiveProfile;
			float leftU = source.ProfileUs[positiveProfile];
			float rightU = source.ProfileUs[negativeProfile];
			var vertices = new Vector3[count * 2];
			var uvs = new Vector2[count * 2];

			for (int i = 0; i < count; i++)
			{
				if ((i & 255) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress();
				}

				int prev = math.max(0, i - 1);
				int next = math.min(count - 1, i + 1);
				float3 right = SweepRibbonSampling.Right3D(path.Tangents[i], path.Ups[i], path.Positions[prev], path.Positions[next]);
				float halfWidth = profileHalf * SampleLut(source.GetWidthLut(splineIndex), path.NormalizedTs[i]);
				float3 left = Elevate(path.Positions[i] + right * halfWidth, source);
				float3 rightPoint = Elevate(path.Positions[i] - right * halfWidth, source);
				int offset = i * 2;
				vertices[offset] = left;
				vertices[offset + 1] = rightPoint;
				float v = path.Stations[i] * source.UvScale;
				uvs[offset] = new Vector2(leftU, v);
				uvs[offset + 1] = new Vector2(rightU, v);
			}

			var triangles = new List<int>((count - 1) * 6);
			for (int i = 0; i + 1 < count; i++)
			{
				int current = i * 2;
				int next = current + 2;
				AddUpward(triangles, vertices, current, next, current + 1);
				AddUpward(triangles, vertices, current + 1, next, next + 1);
			}

			var triangleArray = triangles.ToArray();
			SweepMeshBuilder.Cleanup(ref vertices, ref uvs, ref triangleArray, ct);
			reportProgress();

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangleArray
			};
		}

		private static float3 Elevate(float3 point, SweepSnapshot source)
		{
			point.y += source.HeightOffset;
			return point;
		}

		private static void AddUpward(List<int> triangles, Vector3[] vertices, int a, int b, int c)
		{
			Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
			if (normal.y < 0f)
			{
				triangles.Add(a);
				triangles.Add(c);
				triangles.Add(b);
			}
			else
			{
				triangles.Add(a);
				triangles.Add(b);
				triangles.Add(c);
			}
		}

		private static float SampleLut(float[] lut, float t)
		{
			float f = math.saturate(t) * (lut.Length - 1);
			int i0 = (int)math.floor(f);
			int i1 = math.min(i0 + 1, lut.Length - 1);
			return math.lerp(lut[i0], lut[i1], f - i0);
		}
	}
}
