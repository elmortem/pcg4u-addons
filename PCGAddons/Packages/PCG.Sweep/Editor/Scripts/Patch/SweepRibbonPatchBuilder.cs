using System;
using System.Collections.Generic;
using PCG.Polygons;
using Unity.Mathematics;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	internal static class SweepRibbonPatchBuilder
	{
		internal static List<SweepMeshData> Build(List<SweepRibbonPiece> pieces, List<Spline> splines, SweepSnapshot source, float baseStep)
		{
			var result = new List<SweepMeshData>();

			float profileHalf = math.max(math.abs(source.ProfilePoints[0].x), math.abs(source.ProfilePoints[1].x));

			var polygons = new List<Polygon2D>();
			var centroids = new List<float2>();
			var levels = new List<float>();

			for (int p = 0; p < pieces.Count; p++)
			{
				var piece = pieces[p];
				if (piece.State != SweepRibbonPiece.Red)
					continue;

				var spline = splines[piece.Spline];
				float length = spline.GetLength();
				if (!(length > 1e-4f))
					continue;

				float rangeStart = math.clamp(piece.StartStation, 0f, length);
				float rangeEnd = math.clamp(piece.EndStation, 0f, length);
				var dists = SweepRibbonSampling.AdaptiveStations(spline, rangeStart, rangeEnd, baseStep);
				int n = dists.Count;
				if (n < 2)
					continue;

				var positions = new float3[n];
				var ts = new float[n];
				for (int q = 0; q < n; q++)
				{
					float t = math.saturate(spline.ConvertIndexUnit(dists[q], PathIndexUnit.Distance, PathIndexUnit.Normalized));
					ts[q] = t;
					positions[q] = spline.EvaluatePosition(t);
				}

				var leftPlan = new float2[n];
				var rightPlan = new float2[n];
				float2 centroid = float2.zero;
				float levelSum = 0f;
				for (int q = 0; q < n; q++)
				{
					int prev = math.max(0, q - 1);
					int next = math.min(n - 1, q + 1);
					float3 tangent = spline.EvaluateTangent(ts[q]);
					float3 up = spline.EvaluateUpVector(ts[q]);
					float3 right3 = SweepRibbonSampling.Right3D(tangent, up, positions[prev], positions[next]);
					float halfWidth = profileHalf * SampleLut(source.WidthLut, ts[q]);

					float3 lw = positions[q] + right3 * halfWidth;
					float3 rw = positions[q] - right3 * halfWidth;
					leftPlan[q] = new float2(lw.x, lw.z);
					rightPlan[q] = new float2(rw.x, rw.z);
					centroid += new float2(positions[q].x, positions[q].z);
					levelSum += positions[q].y;
				}

				var outer = new float2[n * 2];
				for (int q = 0; q < n; q++)
					outer[q] = leftPlan[q];
				for (int q = 0; q < n; q++)
					outer[n + q] = rightPlan[n - 1 - q];

				polygons.Add(new Polygon2D { Outer = outer });
				centroids.Add(centroid / n);
				levels.Add(levelSum / n);
			}

			if (polygons.Count == 0)
				return result;

			float overallLevel = 0f;
			for (int i = 0; i < levels.Count; i++)
				overallLevel += levels[i];
			overallLevel /= levels.Count;

			var merged = PolygonClipper.Union(polygons, Array.Empty<Polygon2D>());

			for (int m = 0; m < merged.Count; m++)
			{
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
				if (mesh.Vertices == null || mesh.Vertices.Length < 3 || mesh.Triangles == null || mesh.Triangles.Length < 3)
					continue;

				result.Add(new SweepMeshData
				{
					Vertices = mesh.Vertices,
					Uvs = mesh.Uvs,
					Triangles = mesh.Triangles
				});
			}

			return result;
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
