using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Splines;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepPatchMeshBuilder
	{
		private const float MinimumArea = 1e-10f;
		private const int MaxVertices = 200_000;
		private const float MergeTolerance = 1e-4f;

		internal static bool TryBuild(List<SweepPatchLoop> loops, SweepTerrainWindow terrain, float heightOffset, float step, float uvScale, CancellationToken ct, Action reportProgress, out SweepMeshData mesh, out string failure)
		{
			mesh = default;
			failure = null;

			var vertices = new List<Vector3>();
			var uvs = new List<Vector2>();
			var triangles = new List<int>();
			bool outOfBounds = false;

			for (int l = 0; l < loops.Count; l++)
			{
				ct.ThrowIfCancellationRequested();
				reportProgress();

				if (!TryAppendLoop(loops[l], terrain, heightOffset, step, uvScale, vertices, uvs, triangles, ref outOfBounds, ct, out string loopFailure))
				{
					failure = loopFailure + "-Loop-" + l;
					return false;
				}
			}

			if (triangles.Count == 0)
			{
				failure = "PatchMeshEmpty";
				return false;
			}

			var vertexArray = vertices.ToArray();
			var uvArray = uvs.ToArray();
			var triangleArray = triangles.ToArray();
			SweepMeshBuilder.Cleanup(ref vertexArray, ref uvArray, ref triangleArray, ct);

			mesh = new SweepMeshData
			{
				Vertices = vertexArray,
				Uvs = uvArray,
				Triangles = triangleArray,
				TerrainOutOfBounds = outOfBounds
			};
			return true;
		}

		private static bool TryAppendLoop(SweepPatchLoop loop, SweepTerrainWindow terrain, float heightOffset, float step, float uvScale, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, ref bool outOfBounds, CancellationToken ct, out string failure)
		{
			failure = null;

			var ringPlan = new List<float2>();
			var ringWorld = new List<float3>();

			for (int i = 0; i < loop.Plan.Length; i++)
			{
				float2 plan = loop.Plan[i];
				if (!math.all(math.isfinite(plan)))
				{
					failure = "PatchLoopNonFinite";
					return false;
				}

				if (ringPlan.Count > 0 && math.distance(ringPlan[ringPlan.Count - 1], plan) < MergeTolerance)
					continue;

				ringPlan.Add(plan);
				ringWorld.Add(loop.Points[i]);
			}

			while (ringPlan.Count > 2 && math.distance(ringPlan[0], ringPlan[ringPlan.Count - 1]) < MergeTolerance)
			{
				ringPlan.RemoveAt(ringPlan.Count - 1);
				ringWorld.RemoveAt(ringWorld.Count - 1);
			}

			if (ringPlan.Count < 3)
			{
				failure = "PatchLoopDegenerate";
				return false;
			}

			if (SignedArea(ringPlan) < 0f)
			{
				ringPlan.Reverse();
				ringWorld.Reverse();
			}

			var points = new List<float2>(ringPlan);
			var heights = new List<float>(ringPlan.Count);
			for (int i = 0; i < ringWorld.Count; i++)
				heights.Add(ringWorld[i].y);

			if (!TryTriangulate(points, ringPlan.Count, ct, out int[] boundaryTriangles, out failure))
				return false;

			AppendSteiner(ringPlan, step, terrain, heightOffset, boundaryTriangles, points, heights, ref outOfBounds, ct);

			if (points.Count > MaxVertices)
			{
				failure = "PatchVertexBudgetExceeded";
				return false;
			}

			int[] finalTriangles = boundaryTriangles;
			if (points.Count > ringPlan.Count && !TryTriangulate(points, ringPlan.Count, ct, out finalTriangles, out failure))
				return false;

			int baseIndex = vertices.Count;
			for (int i = 0; i < points.Count; i++)
			{
				vertices.Add(new Vector3(points[i].x, heights[i], points[i].y));
				uvs.Add(new Vector2(points[i].x * uvScale, points[i].y * uvScale));
			}

			for (int i = 0; i < finalTriangles.Length; i += 3)
			{
				triangles.Add(baseIndex + finalTriangles[i]);
				triangles.Add(baseIndex + finalTriangles[i + 2]);
				triangles.Add(baseIndex + finalTriangles[i + 1]);
			}

			return true;
		}

		private static bool TryTriangulate(List<float2> points, int outlineCount, CancellationToken ct, out int[] triangles, out string failure)
		{
			triangles = null;
			failure = null;

			var input = new Vector2[points.Count];
			for (int i = 0; i < points.Count; i++)
				input[i] = new Vector2(points[i].x, points[i].y);

			var outline = new int[outlineCount];
			for (int i = 0; i < outlineCount; i++)
				outline[i] = i;

			var triangulation = new detria.Triangulation();
			triangulation.SetPoints(input);
			triangulation.AddOutline(outline);
			if (!triangulation.Triangulate(true))
			{
				failure = "PatchCdtFailed-" + triangulation.Error.GetType().Name;
				return false;
			}

			var indices = new List<int>();
			foreach (detria.Triangle triangle in triangulation.EnumerateTriangles(false))
			{
				ct.ThrowIfCancellationRequested();

				float orientation = Cross(points[triangle.y] - points[triangle.x], points[triangle.z] - points[triangle.x]);
				if (math.abs(orientation) <= MinimumArea)
					continue;

				indices.Add(triangle.x);
				indices.Add(orientation > 0f ? triangle.y : triangle.z);
				indices.Add(orientation > 0f ? triangle.z : triangle.y);
			}

			if (indices.Count == 0)
			{
				failure = "PatchCdtEmpty";
				return false;
			}

			triangles = indices.ToArray();
			return true;
		}

		private static void AppendSteiner(List<float2> ring, float step, SweepTerrainWindow terrain, float heightOffset, int[] boundaryTriangles, List<float2> points, List<float> heights, ref bool outOfBounds, CancellationToken ct)
		{
			float2 min = new float2(float.MaxValue, float.MaxValue);
			float2 max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < ring.Count; i++)
			{
				min = math.min(min, ring[i]);
				max = math.max(max, ring[i]);
			}

			float spacing = math.max(0.05f, step);
			float margin = spacing * 0.35f;
			float marginSq = margin * margin;

			int countX = (int)math.floor((max.x - min.x) / spacing);
			int countY = (int)math.floor((max.y - min.y) / spacing);
			if (countX < 1 || countY < 1)
				return;

			for (int ix = 1; ix <= countX; ix++)
			{
				for (int iy = 1; iy <= countY; iy++)
				{
					ct.ThrowIfCancellationRequested();

					float2 candidate = new float2(min.x + ix * spacing, min.y + iy * spacing);
					if (!Inside(ring, candidate))
						continue;

					if (BoundaryDistanceSq(ring, candidate) <= marginSq)
						continue;

					float height;
					if (terrain != null)
					{
						if (!terrain.TrySampleHeight(candidate.x, candidate.y, out float sampled))
						{
							outOfBounds = true;
							continue;
						}
						height = sampled + heightOffset;
					}
					else if (!TryInterpolate(points, heights, boundaryTriangles, candidate, out height))
					{
						continue;
					}

					points.Add(candidate);
					heights.Add(height);
				}
			}
		}

		private static bool TryInterpolate(List<float2> points, List<float> heights, int[] triangles, float2 point, out float height)
		{
			height = 0f;

			for (int i = 0; i < triangles.Length; i += 3)
			{
				float2 a = points[triangles[i]];
				float2 b = points[triangles[i + 1]];
				float2 c = points[triangles[i + 2]];

				float area = Cross(b - a, c - a);
				if (math.abs(area) <= MinimumArea)
					continue;

				float wa = Cross(b - point, c - point) / area;
				float wb = Cross(c - point, a - point) / area;
				float wc = Cross(a - point, b - point) / area;
				if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f)
					continue;

				height = wa * heights[triangles[i]] + wb * heights[triangles[i + 1]] + wc * heights[triangles[i + 2]];
				return true;
			}

			return false;
		}

		private static float BoundaryDistanceSq(List<float2> ring, float2 point)
		{
			float best = float.MaxValue;
			for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
				best = math.min(best, SplineNetworkMath.PointSegmentDistanceSq(point, ring[j], ring[i]));
			return best;
		}

		private static bool Inside(List<float2> ring, float2 point)
		{
			bool inside = false;
			for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
			{
				float2 a = ring[i];
				float2 b = ring[j];
				if (a.y > point.y != b.y > point.y)
				{
					float x = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
					if (point.x < x)
						inside = !inside;
				}
			}
			return inside;
		}

		private static float SignedArea(List<float2> ring)
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

		private static float Cross(float2 a, float2 b)
		{
			return a.x * b.y - a.y * b.x;
		}
	}
}
