using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Polygons;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRibbonPatchBuilder
	{
		private struct HeightQuad
		{
			public float2 A;
			public float2 B;
			public float2 C;
			public float2 D;
			public float YA;
			public float YB;
			public float YC;
			public float YD;
		}

		internal static List<SweepMeshData> Build(List<SweepRibbonPiece> pieces, IReadOnlyList<SweepRibbonPath> paths, SweepSnapshot source, CancellationToken ct, Action reportProgress)
		{
			var result = new List<SweepMeshData>();

			float profileHalf = math.max(math.abs(source.ProfilePoints[0].x), math.abs(source.ProfilePoints[1].x));

			var polygons = new List<Polygon2D>();
			var centroids = new List<float2>();
			var levels = new List<float>();
			var quads = new List<HeightQuad>();
			var heightPoints = new List<float3>();

			for (int p = 0; p < pieces.Count; p++)
			{
				ct.ThrowIfCancellationRequested();
				var piece = pieces[p];
				if (piece.State != SweepRibbonPiece.Red)
					continue;

				SweepRibbonPath path = paths[p];
				if (path == null || path.Count < 2 || !(path.Length > 1e-4f))
					continue;

				int n = path.Count;
				float3[] positions = path.Positions;
				float[] ts = path.NormalizedTs;

				var left = new float3[n];
				var right = new float3[n];
				float2 centroid = float2.zero;
				float levelSum = 0f;
				for (int q = 0; q < n; q++)
				{
					int prev = math.max(0, q - 1);
					int next = math.min(n - 1, q + 1);
					float3 tangent = path.Tangents[q];
					float3 up = path.Ups[q];
					float3 right3 = SweepRibbonSampling.Right3D(tangent, up, positions[prev], positions[next]);
					float halfWidth = profileHalf * SampleLut(source.WidthLut, ts[q]);

					float3 center = positions[q];
					center.y += source.HeightOffset;
					left[q] = center + right3 * halfWidth;
					right[q] = center - right3 * halfWidth;
					centroid += new float2(center.x, center.z);
					levelSum += center.y;

					heightPoints.Add(left[q]);
					heightPoints.Add(right[q]);
				}

				for (int q = 0; q + 1 < n; q++)
				{
					quads.Add(new HeightQuad
					{
						A = new float2(left[q].x, left[q].z),
						B = new float2(left[q + 1].x, left[q + 1].z),
						C = new float2(right[q + 1].x, right[q + 1].z),
						D = new float2(right[q].x, right[q].z),
						YA = left[q].y,
						YB = left[q + 1].y,
						YC = right[q + 1].y,
						YD = right[q].y
					});
				}

				var outer = new float2[n * 2];
				for (int q = 0; q < n; q++)
					outer[q] = new float2(left[q].x, left[q].z);
				for (int q = 0; q < n; q++)
					outer[n + q] = new float2(right[n - 1 - q].x, right[n - 1 - q].z);

				polygons.Add(new Polygon2D { Outer = outer });
				centroids.Add(centroid / n);
				levels.Add(levelSum / n);
				reportProgress();
			}

			if (polygons.Count == 0)
				return result;

			float overallLevel = 0f;
			for (int i = 0; i < levels.Count; i++)
				overallLevel += levels[i];
			overallLevel /= levels.Count;

			ct.ThrowIfCancellationRequested();
			var merged = PolygonClipper.Union(polygons, Array.Empty<Polygon2D>());
			ct.ThrowIfCancellationRequested();

			for (int m = 0; m < merged.Count; m++)
			{
				ct.ThrowIfCancellationRequested();
				var region = merged[m];

				float sum = 0f;
				int count = 0;
				for (int i = 0; i < centroids.Count; i++)
				{
					if (region.Contains(centroids[i]))
					{
						sum += levels[i];
						count++;
					}
				}

				float planeY = count > 0 ? sum / count : overallLevel;

				var set = new RegionSet { PlaneY = planeY };
				set.Regions.Add(region);

				var mesh = RegionMeshBuilder.Build(set, null, default, 0f, 0f, 0f, 0, 0f, source.UvScale);
				ct.ThrowIfCancellationRequested();
				if (mesh.Vertices == null || mesh.Vertices.Length < 3 || mesh.Triangles == null || mesh.Triangles.Length < 3)
					continue;

				for (int v = 0; v < mesh.Vertices.Length; v++)
				{
					if ((v & 255) == 0)
						ct.ThrowIfCancellationRequested();
					var vertex = mesh.Vertices[v];
					vertex.y = SampleSurfaceY(new float2(vertex.x, vertex.z), quads, heightPoints, planeY);
					mesh.Vertices[v] = vertex;
				}

				result.Add(new SweepMeshData
				{
					Vertices = mesh.Vertices,
					Uvs = mesh.Uvs,
					Triangles = mesh.Triangles
				});
				reportProgress();
			}

			return result;
		}

		private static float SampleSurfaceY(float2 point, List<HeightQuad> quads, List<float3> heightPoints, float fallback)
		{
			for (int i = 0; i < quads.Count; i++)
			{
				var quad = quads[i];
				if (PointInTriangle(point, quad.A, quad.B, quad.C, quad.YA, quad.YB, quad.YC, out float y))
					return y;
				if (PointInTriangle(point, quad.A, quad.C, quad.D, quad.YA, quad.YC, quad.YD, out y))
					return y;
			}

			float best = fallback;
			float bestDist = float.MaxValue;
			for (int i = 0; i < heightPoints.Count; i++)
			{
				float d = math.distancesq(point, new float2(heightPoints[i].x, heightPoints[i].z));
				if (d < bestDist)
				{
					bestDist = d;
					best = heightPoints[i].y;
				}
			}

			return best;
		}

		private static bool PointInTriangle(float2 p, float2 a, float2 b, float2 c, float ya, float yb, float yc, out float y)
		{
			y = 0f;

			float2 v0 = b - a;
			float2 v1 = c - a;
			float2 v2 = p - a;
			float den = v0.x * v1.y - v1.x * v0.y;
			if (math.abs(den) < 1e-12f)
				return false;

			float v = (v2.x * v1.y - v1.x * v2.y) / den;
			float w = (v0.x * v2.y - v2.x * v0.y) / den;
			float u = 1f - v - w;
			if (u < -1e-3f || v < -1e-3f || w < -1e-3f)
				return false;

			y = u * ya + v * yb + w * yc;
			return true;
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
