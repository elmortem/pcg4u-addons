using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Polygons;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRibbonCornerFanBuilder
	{
		private const float ParallelEpsilon = 1e-12f;
		private const float ParamSlack = 1e-6f;
		private const float BoundaryInsertTolerance = 0.002f;
		private const float EndpointVertexTolerance = 0.005f;

		private struct RibbonQuad
		{
			public float3 A;
			public float3 B;
			public float3 C;
			public float3 D;
			public float2 UvA;
			public float2 UvB;
			public float2 UvC;
			public float2 UvD;
		}

		private struct RibbonTriangle
		{
			public int Quad;
			public float3 A;
			public float3 B;
			public float3 C;
		}

		internal static SweepMeshData Build(SweepRibbonPath path, SweepSnapshot source, float mergeThickness, CancellationToken ct, Action reportProgress)
		{
			if (path == null || path.Count < 2 || !(path.Length > 1e-4f))
				return default;

			float rangeStart = path.Stations[0];
			float rangeEnd = path.Stations[path.Count - 1];
			float span = rangeEnd - rangeStart;
			if (!(span > 1e-4f))
				return default;

			float profileHalf = math.max(math.abs(source.ProfilePoints[0].x), math.abs(source.ProfilePoints[1].x));

			var dists = new List<float>(path.Stations);
			int total = path.Count;
			float3[] positions = path.Positions;
			float[] ts = path.NormalizedTs;

			var left = new float3[total];
			var right = new float3[total];
			var leftPlan = new float2[total];
			var rightPlan = new float2[total];
			bool outOfBounds = false;

			for (int q = 0; q < total; q++)
			{
				int prev = math.max(0, q - 1);
				int next = math.min(total - 1, q + 1);
				float3 tangent = path.Tangents[q];
				float3 up = path.Ups[q];
				float3 right3 = SweepRibbonSampling.Right3D(tangent, up, positions[prev], positions[next]);
				float halfWidth = profileHalf * SampleLut(source.WidthLut, ts[q]);

				float3 lw = positions[q] + right3 * halfWidth;
				float3 rw = positions[q] - right3 * halfWidth;
				float2 lp = new float2(lw.x, lw.z);
				float2 rp = new float2(rw.x, rw.z);

				leftPlan[q] = lp;
				rightPlan[q] = rp;
				left[q] = Elevate(lp, lw.y, source, ref outOfBounds);
				right[q] = Elevate(rp, rw.y, source, ref outOfBounds);
			}

			int positiveProfile = source.ProfilePoints[0].x >= source.ProfilePoints[1].x ? 0 : 1;
			int negativeProfile = 1 - positiveProfile;
			float leftU = source.ProfileUs[positiveProfile];
			float rightU = source.ProfileUs[negativeProfile];
			var quads = BuildQuads(left, right, dists, leftU, rightU, source.UvScale);
			if (!CanCollapseToSingleHeightField(quads, mergeThickness, ct))
				return default;
			var footprint = BuildFootprint(quads, ct);
			if (footprint.Count == 0)
				return default;

			bool innerLeft;
			float2 apexPlan;
			int segA;
			int segB;
			float paramA;
			float paramB;

			SweepMeshData fan = default;
			if (FindSelfIntersection(leftPlan, out apexPlan, out segA, out segB, out paramA, out paramB))
				innerLeft = true;
			else if (FindSelfIntersection(rightPlan, out apexPlan, out segA, out segB, out paramA, out paramB))
				innerLeft = false;
			else
				return BuildUnion(footprint, quads, left, right, dists, leftU, rightU, source, outOfBounds, ct, reportProgress);

			float3[] inner = innerLeft ? left : right;
			float3[] outer = innerLeft ? right : left;

			float apexY = (math.lerp(inner[segA].y, inner[segA + 1].y, paramA) + math.lerp(inner[segB].y, inner[segB + 1].y, paramB)) * 0.5f;
			float3 apex = Elevate(apexPlan, apexY, source, ref outOfBounds);

			float innerU = innerLeft ? leftU : rightU;
			float outerU = innerLeft ? rightU : leftU;

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

			fan = new SweepMeshData
			{
				Vertices = vertsArr,
				Uvs = uvsArr,
				Triangles = trisArr,
				TerrainOutOfBounds = outOfBounds
			};

			if (MatchesFootprint(fan, footprint, ct))
				return fan;

			return BuildUnion(footprint, quads, left, right, dists, leftU, rightU, source, outOfBounds, ct, reportProgress);
		}

		private static List<RibbonQuad> BuildQuads(float3[] left, float3[] right, List<float> dists, float leftU, float rightU, float uvScale)
		{
			var quads = new List<RibbonQuad>(math.max(0, left.Length - 1));
			for (int q = 0; q + 1 < left.Length; q++)
			{
				quads.Add(new RibbonQuad
				{
					A = left[q],
					B = left[q + 1],
					C = right[q + 1],
					D = right[q],
					UvA = new float2(leftU, dists[q] * uvScale),
					UvB = new float2(leftU, dists[q + 1] * uvScale),
					UvC = new float2(rightU, dists[q + 1] * uvScale),
					UvD = new float2(rightU, dists[q] * uvScale)
				});
			}
			return quads;
		}

		private static bool CanCollapseToSingleHeightField(List<RibbonQuad> quads, float mergeThickness, CancellationToken ct)
		{
			var triangles = new List<RibbonTriangle>(quads.Count * 2);
			for (int q = 0; q < quads.Count; q++)
			{
				RibbonQuad quad = quads[q];
				AddHeightTriangle(triangles, q, quad.A, quad.B, quad.C);
				AddHeightTriangle(triangles, q, quad.A, quad.C, quad.D);
			}

			float tolerance = math.max(0f, mergeThickness) + 1e-4f;
			for (int i = 0; i < triangles.Count; i++)
			{
				if ((i & 31) == 0)
					ct.ThrowIfCancellationRequested();
				for (int j = i + 1; j < triangles.Count; j++)
				{
					if (triangles[i].Quad == triangles[j].Quad)
						continue;
					if (!HeightFieldsCompatible(triangles[i], triangles[j], tolerance))
						return false;
				}
			}
			return true;
		}

		private static void AddHeightTriangle(List<RibbonTriangle> triangles, int quad, float3 a, float3 b, float3 c)
		{
			if (math.abs(Cross(Plan(b) - Plan(a), Plan(c) - Plan(a))) <= 1e-8f)
				return;
			triangles.Add(new RibbonTriangle { Quad = quad, A = a, B = b, C = c });
		}

		private static bool HeightFieldsCompatible(RibbonTriangle a, RibbonTriangle b, float tolerance)
		{
			float2 a0 = Plan(a.A);
			float2 a1 = Plan(a.B);
			float2 a2 = Plan(a.C);
			float2 b0 = Plan(b.A);
			float2 b1 = Plan(b.B);
			float2 b2 = Plan(b.C);
			float2 minA = math.min(a0, math.min(a1, a2));
			float2 maxA = math.max(a0, math.max(a1, a2));
			float2 minB = math.min(b0, math.min(b1, b2));
			float2 maxB = math.max(b0, math.max(b1, b2));
			if (maxA.x < minB.x || maxB.x < minA.x || maxA.y < minB.y || maxB.y < minA.y)
				return true;

			var polygon = new List<float2>(6) { a0, a1, a2 };
			var clip = new[] { b0, b1, b2 };
			if (SignedArea(clip) < 0f)
				Array.Reverse(clip);

			for (int edgeIndex = 0; edgeIndex < 3 && polygon.Count > 0; edgeIndex++)
			{
				float2 edgeA = clip[edgeIndex];
				float2 edgeB = clip[(edgeIndex + 1) % 3];
				var output = new List<float2>(polygon.Count + 1);
				float2 previous = polygon[polygon.Count - 1];
				bool previousInside = IsInsideHalfPlane(previous, edgeA, edgeB);
				for (int p = 0; p < polygon.Count; p++)
				{
					float2 current = polygon[p];
					bool currentInside = IsInsideHalfPlane(current, edgeA, edgeB);
					if (currentInside != previousInside && TryLineIntersection(previous, current, edgeA, edgeB, out float2 hit))
						output.Add(hit);
					if (currentInside)
						output.Add(current);
					previous = current;
					previousInside = currentInside;
				}
				polygon = output;
			}

			if (polygon.Count < 3 || math.abs(SignedArea(polygon)) <= 1e-8f)
				return true;

			for (int p = 0; p < polygon.Count; p++)
			{
				if (!TryInterpolateY(polygon[p], a.A, a.B, a.C, out float ay) ||
					!TryInterpolateY(polygon[p], b.A, b.B, b.C, out float by))
					continue;
				if (math.abs(ay - by) > tolerance)
					return false;
			}
			return true;
		}

		private static bool IsInsideHalfPlane(float2 point, float2 edgeA, float2 edgeB)
		{
			return Cross(edgeB - edgeA, point - edgeA) >= -1e-6f;
		}

		private static bool TryLineIntersection(float2 a, float2 b, float2 lineA, float2 lineB, out float2 hit)
		{
			float2 direction = b - a;
			float2 lineDirection = lineB - lineA;
			float denominator = Cross(direction, lineDirection);
			if (math.abs(denominator) <= ParallelEpsilon)
			{
				hit = default;
				return false;
			}
			float t = Cross(lineA - a, lineDirection) / denominator;
			hit = a + direction * t;
			return true;
		}

		private static bool TryInterpolateY(float2 point, float3 a, float3 b, float3 c, out float y)
		{
			float2 pa = Plan(a);
			float2 v0 = Plan(b) - pa;
			float2 v1 = Plan(c) - pa;
			float2 v2 = point - pa;
			float denominator = Cross(v0, v1);
			if (math.abs(denominator) <= ParallelEpsilon)
			{
				y = 0f;
				return false;
			}
			float v = Cross(v2, v1) / denominator;
			float w = Cross(v0, v2) / denominator;
			float u = 1f - v - w;
			y = u * a.y + v * b.y + w * c.y;
			return true;
		}

		private static List<Polygon2D> BuildFootprint(List<RibbonQuad> quads, CancellationToken ct)
		{
			var polygons = new List<Polygon2D>(quads.Count);
			for (int q = 0; q < quads.Count; q++)
			{
				if ((q & 255) == 0)
					ct.ThrowIfCancellationRequested();
				RibbonQuad quad = quads[q];
				var ring = new[] { Plan(quad.A), Plan(quad.B), Plan(quad.C), Plan(quad.D) };
				float area = SignedArea(ring);
				if (math.abs(area) <= 1e-8f)
					continue;
				if (area < 0f)
					Array.Reverse(ring);
				polygons.Add(new Polygon2D { Outer = ring });
			}
			ct.ThrowIfCancellationRequested();
			var footprint = PolygonClipper.Union(polygons, Array.Empty<Polygon2D>());
			ct.ThrowIfCancellationRequested();
			return footprint;
		}

		private static SweepMeshData BuildUnion(List<Polygon2D> footprint, List<RibbonQuad> quads, float3[] left, float3[] right, List<float> dists, float leftU, float rightU, SweepSnapshot source, bool outOfBounds, CancellationToken ct, Action reportProgress)
		{
			float planeY = 0f;
			for (int i = 0; i < left.Length; i++)
				planeY += left[i].y + right[i].y;
			planeY /= math.max(1, left.Length * 2);

			var regions = new RegionSet { PlaneY = planeY };
			for (int i = 0; i < footprint.Count; i++)
				regions.Regions.Add(footprint[i]);

			ct.ThrowIfCancellationRequested();
			var regionMesh = RegionMeshBuilder.Build(regions, null, default, 0f, 0f, 0f, 0, 0f, source.UvScale);
			ct.ThrowIfCancellationRequested();
			if (regionMesh.Vertices == null || regionMesh.Vertices.Length < 3 || regionMesh.Triangles == null || regionMesh.Triangles.Length < 3)
				return default;

			var vertices = regionMesh.Vertices;
			var uvs = regionMesh.Uvs;
			var triangles = regionMesh.Triangles;
			float2 startLeftUv = new float2(leftU, dists[0] * source.UvScale);
			float2 startRightUv = new float2(rightU, dists[0] * source.UvScale);
			float2 endLeftUv = new float2(leftU, dists[dists.Count - 1] * source.UvScale);
			float2 endRightUv = new float2(rightU, dists[dists.Count - 1] * source.UvScale);

			for (int i = 0; i < vertices.Length; i++)
			{
				ct.ThrowIfCancellationRequested();
				float2 plan = new float2(vertices[i].x, vertices[i].z);
				SampleSurface(plan, quads, out float y, out float2 uv);
				float3 point = Elevate(plan, y, source, ref outOfBounds);

				vertices[i] = point;
				uvs[i] = uv;
			}
			InsertEndpoints(ref vertices, ref uvs, ref triangles,
				new[] { left[0], right[0], left[left.Length - 1], right[right.Length - 1] },
				new[] { startLeftUv, startRightUv, endLeftUv, endRightUv });

			EnsureUpward(vertices, triangles);
			SweepMeshBuilder.Cleanup(ref vertices, ref uvs, ref triangles, ct);
			ct.ThrowIfCancellationRequested();
			reportProgress();

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangles,
				TerrainOutOfBounds = outOfBounds
			};
		}

		private static void SampleSurface(float2 point, List<RibbonQuad> quads, out float y, out float2 uv)
		{
			for (int i = 0; i < quads.Count; i++)
			{
				RibbonQuad quad = quads[i];
				if (TryInterpolate(point, quad.A, quad.B, quad.C, quad.UvA, quad.UvB, quad.UvC, out y, out uv))
					return;
				if (TryInterpolate(point, quad.A, quad.C, quad.D, quad.UvA, quad.UvC, quad.UvD, out y, out uv))
					return;
			}

			y = 0f;
			uv = float2.zero;
			float bestDistance = float.MaxValue;
			for (int i = 0; i < quads.Count; i++)
			{
				RibbonQuad quad = quads[i];
				Nearest(point, quad.A, quad.UvA, ref bestDistance, ref y, ref uv);
				Nearest(point, quad.B, quad.UvB, ref bestDistance, ref y, ref uv);
				Nearest(point, quad.C, quad.UvC, ref bestDistance, ref y, ref uv);
				Nearest(point, quad.D, quad.UvD, ref bestDistance, ref y, ref uv);
			}
		}

		private static bool TryInterpolate(float2 point, float3 a, float3 b, float3 c, float2 uvA, float2 uvB, float2 uvC, out float y, out float2 uv)
		{
			float2 pa = Plan(a);
			float2 pb = Plan(b);
			float2 pc = Plan(c);
			float2 v0 = pb - pa;
			float2 v1 = pc - pa;
			float2 v2 = point - pa;
			float denominator = Cross(v0, v1);
			if (math.abs(denominator) < ParallelEpsilon)
			{
				y = 0f;
				uv = float2.zero;
				return false;
			}

			float v = Cross(v2, v1) / denominator;
			float w = Cross(v0, v2) / denominator;
			float u = 1f - v - w;
			if (u < -0.0015f || v < -0.0015f || w < -0.0015f)
			{
				y = 0f;
				uv = float2.zero;
				return false;
			}

			y = u * a.y + v * b.y + w * c.y;
			uv = u * uvA + v * uvB + w * uvC;
			return true;
		}

		private static void Nearest(float2 point, float3 candidate, float2 candidateUv, ref float bestDistance, ref float y, ref float2 uv)
		{
			float distance = math.distancesq(point, Plan(candidate));
			if (distance >= bestDistance)
				return;
			bestDistance = distance;
			y = candidate.y;
			uv = candidateUv;
		}

		private static void InsertEndpoints(ref Vector3[] vertices, ref Vector2[] uvs, ref int[] triangles, float3[] endpoints, float2[] endpointUvs)
		{
			var vertexList = new List<Vector3>(vertices);
			var uvList = new List<Vector2>(uvs);
			var triangleList = new List<int>(triangles);
			var claimed = new HashSet<int>();
			for (int e = 0; e < endpoints.Length; e++)
			{
				int vertex = FindEndpointVertex(vertexList, claimed, endpoints[e]);
				if (vertex >= 0)
				{
					claimed.Add(vertex);
					vertexList[vertex] = endpoints[e];
					uvList[vertex] = endpointUvs[e];
					continue;
				}

				int inserted = vertexList.Count;
				if (FindMeshEdge(vertexList, triangleList, endpoints[e], out int edgeA, out int edgeB))
				{
					vertexList.Add(endpoints[e]);
					uvList.Add(endpointUvs[e]);
					claimed.Add(inserted);
					SplitMeshEdge(triangleList, edgeA, edgeB, inserted);
				}
				else if (FindContainingTriangle(vertexList, triangleList, endpoints[e], out int triangleOffset))
				{
					vertexList.Add(endpoints[e]);
					uvList.Add(endpointUvs[e]);
					claimed.Add(inserted);
					SplitTriangle(triangleList, triangleOffset, inserted);
				}
			}

			vertices = vertexList.ToArray();
			uvs = uvList.ToArray();
			triangles = triangleList.ToArray();
		}

		private static int FindEndpointVertex(List<Vector3> vertices, HashSet<int> claimed, float3 endpoint)
		{
			int best = -1;
			float bestDistance = EndpointVertexTolerance * EndpointVertexTolerance;
			for (int i = 0; i < vertices.Count; i++)
			{
				if (claimed.Contains(i))
					continue;
				float distance = math.distancesq(Plan(vertices[i]), Plan(endpoint));
				if (distance >= bestDistance)
					continue;
				best = i;
				bestDistance = distance;
			}
			return best;
		}

		private static bool FindMeshEdge(List<Vector3> vertices, List<int> triangles, float3 endpoint, out int bestA, out int bestB)
		{
			var counts = new Dictionary<long, int>();
			for (int i = 0; i + 2 < triangles.Count; i += 3)
			{
				CountEdge(counts, triangles[i], triangles[i + 1]);
				CountEdge(counts, triangles[i + 1], triangles[i + 2]);
				CountEdge(counts, triangles[i + 2], triangles[i]);
			}

			bestA = -1;
			bestB = -1;
			float bestDistance = BoundaryInsertTolerance * BoundaryInsertTolerance;
			float2 point = Plan(endpoint);
			foreach (var pair in counts)
			{
				int a = (int)(pair.Key >> 32);
				int b = (int)(uint)pair.Key;
				float2 pa = Plan(vertices[a]);
				float2 pb = Plan(vertices[b]);
				float2 delta = pb - pa;
				float lengthSq = math.lengthsq(delta);
				if (lengthSq <= ParallelEpsilon)
					continue;
				float t = math.dot(point - pa, delta) / lengthSq;
				if (t <= 1e-5f || t >= 1f - 1e-5f)
					continue;
				float distance = math.distancesq(point, math.lerp(pa, pb, t));
				if (distance >= bestDistance)
					continue;
				bestDistance = distance;
				bestA = a;
				bestB = b;
			}
			return bestA >= 0;
		}

		private static void CountEdge(Dictionary<long, int> counts, int a, int b)
		{
			long key = EdgeKey(a, b);
			counts.TryGetValue(key, out int count);
			counts[key] = count + 1;
		}

		private static long EdgeKey(int a, int b)
		{
			int lo = math.min(a, b);
			int hi = math.max(a, b);
			return ((long)lo << 32) | (uint)hi;
		}

		private static void SplitMeshEdge(List<int> triangles, int edgeA, int edgeB, int inserted)
		{
			int originalCount = triangles.Count;
			for (int i = 0; i + 2 < originalCount; i += 3)
			{
				int a = triangles[i];
				int b = triangles[i + 1];
				int c = triangles[i + 2];
				if (EdgeKey(a, b) == EdgeKey(edgeA, edgeB))
				{
					triangles[i + 1] = inserted;
					triangles.Add(inserted);
					triangles.Add(b);
					triangles.Add(c);
					continue;
				}
				if (EdgeKey(b, c) == EdgeKey(edgeA, edgeB))
				{
					triangles[i + 2] = inserted;
					triangles.Add(a);
					triangles.Add(inserted);
					triangles.Add(c);
					continue;
				}
				if (EdgeKey(c, a) == EdgeKey(edgeA, edgeB))
				{
					triangles[i + 2] = inserted;
					triangles.Add(inserted);
					triangles.Add(b);
					triangles.Add(c);
				}
			}
		}

		private static bool FindContainingTriangle(List<Vector3> vertices, List<int> triangles, float3 endpoint, out int triangleOffset)
		{
			float2 point = Plan(endpoint);
			for (int i = 0; i + 2 < triangles.Count; i += 3)
			{
				float2 a = Plan(vertices[triangles[i]]);
				float2 b = Plan(vertices[triangles[i + 1]]);
				float2 c = Plan(vertices[triangles[i + 2]]);
				float denominator = Cross(b - a, c - a);
				if (math.abs(denominator) <= ParallelEpsilon)
					continue;
				float v = Cross(point - a, c - a) / denominator;
				float w = Cross(b - a, point - a) / denominator;
				float u = 1f - v - w;
				if (u < -1e-5f || v < -1e-5f || w < -1e-5f)
					continue;
				triangleOffset = i;
				return true;
			}
			triangleOffset = -1;
			return false;
		}

		private static void SplitTriangle(List<int> triangles, int offset, int inserted)
		{
			int a = triangles[offset];
			int b = triangles[offset + 1];
			int c = triangles[offset + 2];
			triangles[offset + 2] = inserted;
			triangles.Add(b);
			triangles.Add(c);
			triangles.Add(inserted);
			triangles.Add(c);
			triangles.Add(a);
			triangles.Add(inserted);
		}

		private static void EnsureUpward(Vector3[] vertices, int[] triangles)
		{
			for (int i = 0; i + 2 < triangles.Length; i += 3)
			{
				float3 a = vertices[triangles[i]];
				float3 b = vertices[triangles[i + 1]];
				float3 c = vertices[triangles[i + 2]];
				if (math.cross(b - a, c - a).y >= 0f)
					continue;
				int swap = triangles[i + 1];
				triangles[i + 1] = triangles[i + 2];
				triangles[i + 2] = swap;
			}
		}

		private static bool MatchesFootprint(SweepMeshData mesh, List<Polygon2D> footprint, CancellationToken ct)
		{
			if (mesh.Vertices == null || mesh.Triangles == null || mesh.Triangles.Length < 3)
				return false;

			var triangles = new List<Polygon2D>(mesh.Triangles.Length / 3);
			double triangleArea = 0d;
			for (int i = 0; i + 2 < mesh.Triangles.Length; i += 3)
			{
				if (((i / 3) & 255) == 0)
					ct.ThrowIfCancellationRequested();
				float2 a = Plan(mesh.Vertices[mesh.Triangles[i]]);
				float2 b = Plan(mesh.Vertices[mesh.Triangles[i + 1]]);
				float2 c = Plan(mesh.Vertices[mesh.Triangles[i + 2]]);
				float area = Cross(b - a, c - a) * 0.5f;
				if (math.abs(area) <= 1e-8f)
					return false;
				triangleArea += math.abs(area);
				var ring = area > 0f ? new[] { a, b, c } : new[] { a, c, b };
				triangles.Add(new Polygon2D { Outer = ring });
			}

			ct.ThrowIfCancellationRequested();
			var triangleUnion = PolygonClipper.Union(triangles, Array.Empty<Polygon2D>());
			ct.ThrowIfCancellationRequested();
			double footprintArea = Area(footprint);
			double unionArea = Area(triangleUnion);
			double gridArea = 4.0 / (PolygonClipper.Scale * PolygonClipper.Scale);
			double tolerance = Math.Max(gridArea, footprintArea * 1e-4);
			if (triangleArea - unionArea > tolerance)
				return false;

			double leak = Area(PolygonClipper.Difference(triangleUnion, footprint));
			ct.ThrowIfCancellationRequested();
			double missing = Area(PolygonClipper.Difference(footprint, triangleUnion));
			ct.ThrowIfCancellationRequested();
			return leak <= tolerance && missing <= tolerance;
		}

		private static double Area(List<Polygon2D> polygons)
		{
			double area = 0d;
			for (int i = 0; i < polygons.Count; i++)
			{
				Polygon2D polygon = polygons[i];
				area += math.abs(SignedArea(polygon.Outer));
				for (int h = 0; h < polygon.Holes.Count; h++)
					area -= math.abs(SignedArea(polygon.Holes[h]));
			}
			return area;
		}

		private static float SignedArea(IReadOnlyList<float2> ring)
		{
			float area = 0f;
			for (int i = 0; i < ring.Count; i++)
			{
				float2 a = ring[i];
				float2 b = ring[(i + 1) % ring.Count];
				area += Cross(a, b);
			}
			return area * 0.5f;
		}

		private static float2 Plan(float3 point)
		{
			return new float2(point.x, point.z);
		}

		private static float2 Plan(Vector3 point)
		{
			return new float2(point.x, point.z);
		}

		private static float Cross(float2 a, float2 b)
		{
			return a.x * b.y - a.y * b.x;
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
