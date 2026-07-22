using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	internal static class SweepRibbonCornerFanBuilder
	{
		private const int MinSamples = 8;
		private const float ParallelEpsilon = 1e-12f;
		private const float ParamSlack = 1e-6f;

		internal static SweepMeshData Build(Spline spline, float startStation, float endStation, SweepSnapshot source, float baseStep, CancellationToken ct, Action reportProgress)
		{
			float length = spline.GetLength();
			if (!(length > 1e-4f))
				return default;

			float rangeStart = math.clamp(startStation, 0f, length);
			float rangeEnd = math.clamp(endStation, 0f, length);
			float span = rangeEnd - rangeStart;
			if (!(span > 1e-4f))
				return default;

			float profileHalf = math.max(math.abs(source.ProfilePoints[0].x), math.abs(source.ProfilePoints[1].x));

			var dists = SweepRibbonSampling.AdaptiveStations(spline, rangeStart, rangeEnd, baseStep);
			if (dists.Count < MinSamples)
				dists = Uniform(rangeStart, rangeEnd, MinSamples);
			int total = dists.Count;

			var positions = new float3[total];
			var ts = new float[total];
			for (int q = 0; q < total; q++)
			{
				float t = math.saturate(spline.ConvertIndexUnit(dists[q], PathIndexUnit.Distance, PathIndexUnit.Normalized));
				ts[q] = t;
				positions[q] = spline.EvaluatePosition(t);
			}

			var left = new float3[total];
			var right = new float3[total];
			var leftPlan = new float2[total];
			var rightPlan = new float2[total];
			bool outOfBounds = false;

			for (int q = 0; q < total; q++)
			{
				int prev = math.max(0, q - 1);
				int next = math.min(total - 1, q + 1);
				float3 tangent = spline.EvaluateTangent(ts[q]);
				float3 up = spline.EvaluateUpVector(ts[q]);
				float2 planRight = SweepRibbonSampling.PlanRight(tangent, up, positions[prev], positions[next]);
				float halfWidth = profileHalf * SampleLut(source.WidthLut, ts[q]);

				float2 centerPlan = new float2(positions[q].x, positions[q].z);
				float2 lp = centerPlan + planRight * halfWidth;
				float2 rp = centerPlan - planRight * halfWidth;

				leftPlan[q] = lp;
				rightPlan[q] = rp;
				left[q] = Elevate(lp, positions[q].y, source, ref outOfBounds);
				right[q] = Elevate(rp, positions[q].y, source, ref outOfBounds);
			}

			bool innerLeft;
			float2 apexPlan;
			int segA;
			int segB;
			float paramA;
			float paramB;

			if (FindSelfIntersection(leftPlan, out apexPlan, out segA, out segB, out paramA, out paramB))
				innerLeft = true;
			else if (FindSelfIntersection(rightPlan, out apexPlan, out segA, out segB, out paramA, out paramB))
				innerLeft = false;
			else
				return default;

			float3[] inner = innerLeft ? left : right;
			float3[] outer = innerLeft ? right : left;

			float apexY = (math.lerp(inner[segA].y, inner[segA + 1].y, paramA) + math.lerp(inner[segB].y, inner[segB + 1].y, paramB)) * 0.5f;
			float3 apex = Elevate(apexPlan, apexY, source, ref outOfBounds);

			float innerU = innerLeft ? source.ProfileUs[0] : source.ProfileUs[1];
			float outerU = innerLeft ? source.ProfileUs[1] : source.ProfileUs[0];

			var vertices = new List<Vector3>(total + 3);
			var uvs = new List<Vector2>(total + 3);

			vertices.Add(apex);
			uvs.Add(new Vector2(innerU, (rangeStart + rangeEnd) * 0.5f * source.UvScale));

			for (int q = 0; q < total; q++)
			{
				vertices.Add(outer[q]);
				uvs.Add(new Vector2(outerU, dists[q] * source.UvScale));
			}

			int innerStartIdx = vertices.Count;
			vertices.Add(inner[0]);
			uvs.Add(new Vector2(innerU, rangeStart * source.UvScale));

			int innerEndIdx = vertices.Count;
			vertices.Add(inner[total - 1]);
			uvs.Add(new Vector2(innerU, rangeEnd * source.UvScale));

			var vertsArr = vertices.ToArray();
			var triangles = new List<int>((total + 1) * 3);

			for (int q = 0; q + 1 < total; q++)
				AddUpward(triangles, vertsArr, 0, 1 + q, 2 + q);

			AddUpward(triangles, vertsArr, 0, innerStartIdx, 1);
			AddUpward(triangles, vertsArr, 0, total, innerEndIdx);

			var uvsArr = uvs.ToArray();
			var trisArr = triangles.ToArray();
			SweepMeshBuilder.Cleanup(ref vertsArr, ref uvsArr, ref trisArr, ct);

			reportProgress();

			return new SweepMeshData
			{
				Vertices = vertsArr,
				Uvs = uvsArr,
				Triangles = trisArr,
				TerrainOutOfBounds = outOfBounds
			};
		}

		private static List<float> Uniform(float start, float end, int count)
		{
			var dists = new List<float>(count + 1);
			for (int q = 0; q <= count; q++)
				dists.Add(math.lerp(start, end, q / (float)count));
			return dists;
		}

		private static float3 Elevate(float2 plan, float fallbackY, SweepSnapshot source, ref bool outOfBounds)
		{
			float y = fallbackY;
			if (source.Terrain != null)
			{
				if (source.Terrain.TrySampleHeight(plan.x, plan.y, out float h))
					y = h + source.HeightOffset;
				else
					outOfBounds = true;
			}
			return new float3(plan.x, y, plan.y);
		}

		private static void AddUpward(List<int> triangles, Vector3[] vertices, int a, int b, int c)
		{
			float3 pa = vertices[a];
			float3 pb = vertices[b];
			float3 pc = vertices[c];
			float3 normal = math.cross(pb - pa, pc - pa);
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

		private static bool FindSelfIntersection(float2[] points, out float2 hit, out int segA, out int segB, out float paramA, out float paramB)
		{
			hit = default;
			segA = -1;
			segB = -1;
			paramA = 0f;
			paramB = 0f;

			int n = points.Length;
			for (int i = 0; i + 1 < n; i++)
			{
				for (int j = i + 2; j + 1 < n; j++)
				{
					if (TrySegmentIntersection(points[i], points[i + 1], points[j], points[j + 1], out float ta, out float tb))
					{
						hit = math.lerp(points[i], points[i + 1], ta);
						segA = i;
						segB = j;
						paramA = ta;
						paramB = tb;
						return true;
					}
				}
			}

			return false;
		}

		private static bool TrySegmentIntersection(float2 a0, float2 a1, float2 b0, float2 b1, out float ta, out float tb)
		{
			ta = 0f;
			tb = 0f;

			float2 d1 = a1 - a0;
			float2 d2 = b1 - b0;
			float den = d1.x * d2.y - d1.y * d2.x;
			if (math.abs(den) < ParallelEpsilon)
				return false;

			float2 dp = b0 - a0;
			ta = (dp.x * d2.y - dp.y * d2.x) / den;
			tb = (dp.x * d1.y - dp.y * d1.x) / den;
			return ta >= ParamSlack && ta <= 1f - ParamSlack && tb >= ParamSlack && tb <= 1f - ParamSlack;
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
