using System;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepProfileMeshBuilder
	{
		internal static SweepMeshData Build(SweepRibbonPath path, SweepSnapshot source, CancellationToken ct, Action reportProgress)
		{
			if (path == null || path.Count < 2 || !(path.Length > 1e-4f) ||
				source.ProfilePoints == null || source.ProfilePoints.Length < 2 ||
				source.ProfileUs == null || source.ProfileUs.Length != source.ProfilePoints.Length ||
				source.ProfileSegments == null || source.ProfileSegments.Length < 2)
				return default;

			int ringCount = path.Count;
			int verticesPerRing = source.ProfilePoints.Length;
			int segmentCount = source.ProfileSegments.Length / 2;
			var vertices = new Vector3[ringCount * verticesPerRing];
			var uvs = new Vector2[vertices.Length];

			for (int i = 0; i < ringCount; i++)
			{
				if ((i & 127) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress();
				}

				int prev = math.max(0, i - 1);
				int next = math.min(ringCount - 1, i + 1);
				float3 tangent = math.normalizesafe(path.Tangents[i], path.Positions[next] - path.Positions[prev]);
				float3 right = SweepRibbonSampling.Right3D(tangent, path.Ups[i], path.Positions[prev], path.Positions[next]);
				float3 up = math.normalizesafe(math.cross(tangent, right), path.Ups[i]);
				float widthMul = SampleLut(source.WidthLut, path.NormalizedTs[i]);
				float heightMul = SampleLut(source.HeightLut, path.NormalizedTs[i]);
				float twist = math.radians(SampleLut(source.TwistLut, path.NormalizedTs[i]));
				float twistCos = math.cos(twist);
				float twistSin = math.sin(twist);

				for (int j = 0; j < verticesPerRing; j++)
				{
					float2 point = source.ProfilePoints[j];
					float px = point.x * widthMul;
					float py = point.y * heightMul;
					float rx = px * twistCos - py * twistSin;
					float ry = px * twistSin + py * twistCos;
					float3 position = path.Positions[i] + right * rx + up * ry;
					position.y += source.HeightOffset;

					int index = i * verticesPerRing + j;
					vertices[index] = position;
					uvs[index] = new Vector2(source.ProfileUs[j], path.Stations[i] * source.UvScale);
				}
			}

			var triangles = new int[(ringCount - 1) * segmentCount * 6];
			int triangleIndex = 0;
			for (int i = 0; i + 1 < ringCount; i++)
			{
				int nextRing = i + 1;
				for (int segment = 0; segment < segmentCount; segment++)
				{
					int a = source.ProfileSegments[segment * 2];
					int b = source.ProfileSegments[segment * 2 + 1];
					int currentA = i * verticesPerRing + a;
					int currentB = i * verticesPerRing + b;
					int nextA = nextRing * verticesPerRing + a;
					int nextB = nextRing * verticesPerRing + b;
					triangles[triangleIndex++] = currentA;
					triangles[triangleIndex++] = nextA;
					triangles[triangleIndex++] = currentB;
					triangles[triangleIndex++] = currentB;
					triangles[triangleIndex++] = nextA;
					triangles[triangleIndex++] = nextB;
				}
			}

			var startRing = new Vector3[verticesPerRing];
			var endRing = new Vector3[verticesPerRing];
			int endOffset = (ringCount - 1) * verticesPerRing;
			for (int j = 0; j < verticesPerRing; j++)
			{
				startRing[j] = vertices[j];
				endRing[j] = vertices[endOffset + j];
			}

			SweepMeshBuilder.Cleanup(ref vertices, ref uvs, ref triangles, ct);
			reportProgress();

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangles,
				StartRing = startRing,
				EndRing = endRing
			};
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
