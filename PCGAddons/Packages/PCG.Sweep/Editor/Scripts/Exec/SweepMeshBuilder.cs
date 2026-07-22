using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	public static class SweepMeshBuilder
	{
		private const float MinTriangleArea = 1e-8f;
		private const float TurnLimit = 4.712389f;
		private const float BridgeToleranceFactor = 0.5f;
		private const float ParallelEpsilon = 1e-12f;
		private const float ParamSlack = 1e-6f;
		private const float MinCellSize = 1e-3f;

		public static SweepMeshData Build(SweepSnapshot snapshot, int splineIndex, CancellationToken ct, Action reportProgress)
		{
			var frames = snapshot.Frames[splineIndex];
			bool splineClosed = snapshot.SplineClosed[splineIndex];
			int ringCount = frames.Length;
			int vpr = snapshot.ProfilePoints.Length;

			bool applyFront = snapshot.CapStartFlags[splineIndex] && snapshot.ProfileClosed && !splineClosed;
			bool applyBack = snapshot.CapEndFlags[splineIndex] && snapshot.ProfileClosed && !splineClosed;
			List<float2> outline = null;
			int[] outlineProfileIndex = null;
			List<int> capTriangles = null;
			if (applyFront || applyBack)
			{
				outline = ExtractOutline(snapshot.ProfilePoints, snapshot.ProfileSegments);
				if (outline.Count >= 3)
				{
					outlineProfileIndex = MapOutlineToProfile(outline, snapshot.ProfilePoints);
					capTriangles = Triangulate(outline);
				}
				else
				{
					applyFront = false;
					applyBack = false;
				}
			}

			int enabledSides = (applyFront ? 1 : 0) + (applyBack ? 1 : 0);
			int capVertexCount = enabledSides * (outline?.Count ?? 0);
			int capIndexCount = enabledSides * (capTriangles?.Count ?? 0);

			int segPairs = snapshot.ProfileSegments.Length / 2;
			int ringPairs = ringCount - 1;

			int ringVertexCount = ringCount * vpr;
			int totalVertexCount = ringVertexCount + capVertexCount;

			var positions = BuildRingPositions(snapshot, splineIndex, ct, reportProgress, out bool outOfBounds);

			var uvs = new Vector2[totalVertexCount];
			for (int i = 0; i < ringCount; i++)
			{
				float v = frames[i].Distance * snapshot.UvScale;
				for (int j = 0; j < vpr; j++)
					uvs[i * vpr + j] = new Vector2(snapshot.ProfileUs[j], v);
			}

			var startRing = new Vector3[vpr];
			var endRing = new Vector3[vpr];
			int lastRingOffset = (ringCount - 1) * vpr;
			for (int j = 0; j < vpr; j++)
			{
				float3 start = positions[j];
				float3 end = positions[lastRingOffset + j];
				startRing[j] = new Vector3(start.x, start.y, start.z);
				endRing[j] = new Vector3(end.x, end.y, end.z);
			}

			var vertices = new Vector3[totalVertexCount];
			for (int idx = 0; idx < ringVertexCount; idx++)
			{
				float3 p = positions[idx];
				vertices[idx] = new Vector3(p.x, p.y, p.z);
			}

			var triangles = new int[ringPairs * segPairs * 6 + capIndexCount];
			int k = 0;
			for (int i = 0; i < ringPairs; i++)
			{
				int i1 = i + 1;
				for (int s = 0; s < segPairs; s++)
				{
					int a = snapshot.ProfileSegments[s * 2];
					int b = snapshot.ProfileSegments[s * 2 + 1];
					int ia = i * vpr + a;
					int ib = i * vpr + b;
					int ja = i1 * vpr + a;
					int jb = i1 * vpr + b;
					triangles[k++] = ia;
					triangles[k++] = ja;
					triangles[k++] = ib;
					triangles[k++] = ib;
					triangles[k++] = ja;
					triangles[k++] = jb;
				}
			}

			if (applyFront || applyBack)
			{
				ct.ThrowIfCancellationRequested();
				int frontBase = ringCount * vpr;
				int backBase = applyFront ? frontBase + outline.Count : ringCount * vpr;
				int lastRing = ringCount - 1;

				if (applyFront)
				{
					for (int o = 0; o < outline.Count; o++)
					{
						int profileIndex = outlineProfileIndex[o];
						vertices[frontBase + o] = vertices[profileIndex];
						float2 uv = snapshot.ProfilePoints[profileIndex] * snapshot.UvScale;
						uvs[frontBase + o] = new Vector2(uv.x, uv.y);
					}

					for (int c = 0; c < capTriangles.Count; c += 3)
					{
						int o0 = capTriangles[c];
						int o1 = capTriangles[c + 1];
						int o2 = capTriangles[c + 2];

						triangles[k++] = frontBase + o0;
						triangles[k++] = frontBase + o2;
						triangles[k++] = frontBase + o1;
					}
				}

				if (applyBack)
				{
					for (int o = 0; o < outline.Count; o++)
					{
						int profileIndex = outlineProfileIndex[o];
						vertices[backBase + o] = vertices[lastRing * vpr + profileIndex];
						float2 uv = snapshot.ProfilePoints[profileIndex] * snapshot.UvScale;
						uvs[backBase + o] = new Vector2(uv.x, uv.y);
					}

					for (int c = 0; c < capTriangles.Count; c += 3)
					{
						int o0 = capTriangles[c];
						int o1 = capTriangles[c + 1];
						int o2 = capTriangles[c + 2];

						triangles[k++] = backBase + o0;
						triangles[k++] = backBase + o1;
						triangles[k++] = backBase + o2;
					}
				}
			}

			Cleanup(ref vertices, ref uvs, ref triangles, ct);

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangles,
				StartRing = startRing,
				EndRing = endRing,
				TerrainOutOfBounds = outOfBounds
			};
		}

		internal static float3[] BuildRingPositions(SweepSnapshot snapshot, int splineIndex, CancellationToken ct, Action reportProgress, out bool outOfBounds)
		{
			var frames = snapshot.Frames[splineIndex];
			bool splineClosed = snapshot.SplineClosed[splineIndex];
			int ringCount = frames.Length;
			int vpr = snapshot.ProfilePoints.Length;
			int ringVertexCount = ringCount * vpr;

			var rights = new float3[ringCount];
			var ups = new float3[ringCount];
			BuildBasis(frames, rights, ups);

			var terrain = snapshot.Terrain;
			bool hasTerrain = terrain != null;

			var positions = new float3[ringVertexCount];
			float[] verticalOffsets = hasTerrain ? new float[ringVertexCount] : null;

			int progressCounter = 0;

			for (int i = 0; i < ringCount; i++)
			{
				float t = frames[i].T;
				float widthMul = SampleLut(snapshot.WidthLut, t);
				float heightMul = SampleLut(snapshot.HeightLut, t);
				float twist = math.radians(SampleLut(snapshot.TwistLut, t));
				float twistCos = math.cos(twist);
				float twistSin = math.sin(twist);

				float3 basePos = frames[i].Position;
				float3 right = rights[i];
				float3 up = ups[i];
				float2 rightXz = math.normalizesafe(new float2(right.x, right.z), new float2(1f, 0f));

				for (int j = 0; j < vpr; j++)
				{
					float2 point = snapshot.ProfilePoints[j];
					float px = point.x * widthMul;
					float py = point.y * heightMul;
					float rx = px * twistCos - py * twistSin;
					float ry = px * twistSin + py * twistCos;

					int idx = i * vpr + j;
					if (!hasTerrain)
					{
						positions[idx] = basePos + right * rx + up * ry;
					}
					else
					{
						positions[idx] = new float3(basePos.x + rightXz.x * rx, basePos.y + ry, basePos.z + rightXz.y * rx);
						verticalOffsets[idx] = ry;
					}

					progressCounter++;
					if (progressCounter % 1024 == 0)
					{
						ct.ThrowIfCancellationRequested();
						reportProgress();
					}
				}
			}

			TrimColumns(frames, ups, positions, vpr, splineClosed, snapshot.MaxLateralExtent, ct, reportProgress);

			outOfBounds = false;
			if (hasTerrain)
			{
				for (int idx = 0; idx < ringVertexCount; idx++)
				{
					float3 p = positions[idx];
					if (terrain.TrySampleHeight(p.x, p.z, out float h))
					{
						p.y = h + snapshot.HeightOffset + verticalOffsets[idx];
						positions[idx] = p;
					}
					else
					{
						outOfBounds = true;
					}

					progressCounter++;
					if (progressCounter % 1024 == 0)
					{
						ct.ThrowIfCancellationRequested();
						reportProgress();
					}
				}
			}

			return positions;
		}

		private static void TrimColumns(SweepFrame[] frames, float3[] ups, float3[] positions, int vpr, bool closed, float lateralExtent, CancellationToken ct, Action reportProgress)
		{
			int ringCount = frames.Length;
			int cycleCount = closed ? ringCount - 1 : ringCount;
			if (cycleCount < 3)
				return;

			var normals = new float3[cycleCount];
			for (int i = 0; i < cycleCount; i++)
				normals[i] = math.normalizesafe(frames[i].Tangent, new float3(0f, 0f, 1f));

			float3 axis = float3.zero;
			for (int i = 0; i < cycleCount; i++)
				axis += ups[i];
			axis = math.normalizesafe(axis, new float3(0f, 1f, 0f));
			float3 helper = math.abs(axis.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
			float3 e1 = math.normalize(math.cross(axis, helper));
			float3 e2 = math.cross(axis, e1);

			int segCount = closed ? cycleCount : cycleCount - 1;
			var turnAt = new float[cycleCount + 1];
			for (int i = 1; i <= cycleCount; i++)
			{
				int a = (i - 1) % cycleCount;
				int b = i % cycleCount;
				turnAt[i] = turnAt[i - 1] + math.acos(math.clamp(math.dot(normals[a], normals[b]), -1f, 1f));
			}

			float bridgeTolerance = lateralExtent * BridgeToleranceFactor;
			int progressCounter = 0;

			var projected = new float2[cycleCount];
			var cells = new Dictionary<long, List<int>>();
			var candidates = new List<int>();

			int runCount = closed ? 2 : 1;
			int shift = closed ? cycleCount / 2 : 0;

			for (int j = 0; j < vpr; j++)
			{
				for (int run = 0; run < runCount; run++)
				{
					int origin = run == 0 ? 0 : shift;

					for (int i = 0; i < cycleCount; i++)
					{
						float3 p = positions[RingIndex(i, origin, cycleCount) * vpr + j];
						projected[i] = new float2(math.dot(p, e1), math.dot(p, e2));
					}

					float cellSize = MinCellSize;
					for (int s = 0; s < segCount; s++)
						cellSize = math.max(cellSize, math.distance(projected[s], projected[(s + 1) % cycleCount]));

					cells.Clear();
					for (int s = 0; s < segCount; s++)
						InsertSegment(cells, projected[s], projected[(s + 1) % cycleCount], cellSize, s);

					int current = 0;
					while (current < segCount - 2)
					{
						float2 a0 = projected[current];
						float2 a1 = projected[(current + 1) % cycleCount];
						if (math.distancesq(a0, a1) < ParallelEpsilon)
						{
							current++;
							continue;
						}

						CollectCandidates(cells, a0, a1, cellSize, candidates);
						candidates.Sort();

						int hitSegment = -1;
						float3 hitPoint = float3.zero;
						foreach (int k in candidates)
						{
							if (k <= current + 1 || k >= segCount)
								continue;
							if (closed && current == 0 && k == segCount - 1)
								continue;
							if (turnAt[k] - turnAt[current] > TurnLimit)
								continue;

							float2 b0 = projected[k];
							float2 b1 = projected[(k + 1) % cycleCount];
							if (math.distancesq(b0, b1) < ParallelEpsilon)
								continue;

							progressCounter++;
							if (progressCounter % 1024 == 0)
							{
								ct.ThrowIfCancellationRequested();
								reportProgress();
							}

							if (!TrySegmentIntersection(a0, a1, b0, b1, out float ta, out float tb))
								continue;

							int ia = RingIndex(current, origin, cycleCount) * vpr + j;
							int ia1 = RingIndex((current + 1) % cycleCount, origin, cycleCount) * vpr + j;
							int ib = RingIndex(k, origin, cycleCount) * vpr + j;
							int ib1 = RingIndex((k + 1) % cycleCount, origin, cycleCount) * vpr + j;
							float3 pa = math.lerp(positions[ia], positions[ia1], ta);
							float3 pb = math.lerp(positions[ib], positions[ib1], tb);
							if (math.distance(pa, pb) > bridgeTolerance)
								continue;

							hitSegment = k;
							hitPoint = (pa + pb) * 0.5f;
							break;
						}

						if (hitSegment < 0)
						{
							current++;
							continue;
						}

						float2 snapProjected = new float2(math.dot(hitPoint, e1), math.dot(hitPoint, e2));
						for (int m = current + 1; m <= hitSegment; m++)
						{
							positions[RingIndex(m, origin, cycleCount) * vpr + j] = hitPoint;
							projected[m] = snapProjected;
						}

						current = hitSegment;
					}
				}
			}

			if (closed)
			{
				for (int j = 0; j < vpr; j++)
					positions[(ringCount - 1) * vpr + j] = positions[j];
			}
		}

		private static int RingIndex(int index, int origin, int cycleCount)
		{
			int shifted = index + origin;
			if (shifted >= cycleCount)
				shifted -= cycleCount;
			return shifted;
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
			return ta >= -ParamSlack && ta <= 1f + ParamSlack && tb >= -ParamSlack && tb <= 1f + ParamSlack;
		}

		private static void InsertSegment(Dictionary<long, List<int>> cells, float2 a, float2 b, float cellSize, int segment)
		{
			int x0 = (int)math.floor(math.min(a.x, b.x) / cellSize);
			int x1 = (int)math.floor(math.max(a.x, b.x) / cellSize);
			int y0 = (int)math.floor(math.min(a.y, b.y) / cellSize);
			int y1 = (int)math.floor(math.max(a.y, b.y) / cellSize);

			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					long key = ((long)x << 32) ^ (uint)y;
					if (!cells.TryGetValue(key, out var list))
					{
						list = new List<int>();
						cells.Add(key, list);
					}
					list.Add(segment);
				}
			}
		}

		private static void CollectCandidates(Dictionary<long, List<int>> cells, float2 a, float2 b, float cellSize, List<int> candidates)
		{
			candidates.Clear();
			int x0 = (int)math.floor(math.min(a.x, b.x) / cellSize) - 1;
			int x1 = (int)math.floor(math.max(a.x, b.x) / cellSize) + 1;
			int y0 = (int)math.floor(math.min(a.y, b.y) / cellSize) - 1;
			int y1 = (int)math.floor(math.max(a.y, b.y) / cellSize) + 1;

			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					long key = ((long)x << 32) ^ (uint)y;
					if (cells.TryGetValue(key, out var list))
					{
						foreach (int segment in list)
						{
							if (!candidates.Contains(segment))
								candidates.Add(segment);
						}
					}
				}
			}
		}

		internal static void Cleanup(ref Vector3[] vertices, ref Vector2[] uvs, ref int[] triangles, CancellationToken ct)
		{
			int vertexCount = vertices.Length;
			var remap = new int[vertexCount];
			var weld = new Dictionary<SweepWeldKey, int>(vertexCount);
			for (int i = 0; i < vertexCount; i++)
			{
				var key = new SweepWeldKey(vertices[i], uvs[i]);
				if (weld.TryGetValue(key, out int first))
				{
					remap[i] = first;
				}
				else
				{
					weld.Add(key, i);
					remap[i] = i;
				}
			}

			var compact = new int[vertexCount];
			for (int i = 0; i < vertexCount; i++)
				compact[i] = -1;

			var newVertices = new List<Vector3>(vertexCount);
			var newUvs = new List<Vector2>(vertexCount);
			var newTriangles = new List<int>(triangles.Length);

			int triCount = triangles.Length / 3;
			for (int tri = 0; tri < triCount; tri++)
			{
				if (tri % 1024 == 0)
					ct.ThrowIfCancellationRequested();

				int i0 = remap[triangles[tri * 3]];
				int i1 = remap[triangles[tri * 3 + 1]];
				int i2 = remap[triangles[tri * 3 + 2]];

				if (i0 == i1 || i1 == i2 || i0 == i2)
					continue;

				float3 a = vertices[i0];
				float3 b = vertices[i1];
				float3 c = vertices[i2];
				float area = 0.5f * math.length(math.cross(b - a, c - a));
				if (area < MinTriangleArea)
					continue;

				newTriangles.Add(EmitVertex(i0, compact, vertices, uvs, newVertices, newUvs));
				newTriangles.Add(EmitVertex(i1, compact, vertices, uvs, newVertices, newUvs));
				newTriangles.Add(EmitVertex(i2, compact, vertices, uvs, newVertices, newUvs));
			}

			vertices = newVertices.ToArray();
			uvs = newUvs.ToArray();
			triangles = newTriangles.ToArray();
		}

		private static int EmitVertex(int welded, int[] compact, Vector3[] vertices, Vector2[] uvs, List<Vector3> newVertices, List<Vector2> newUvs)
		{
			if (compact[welded] < 0)
			{
				compact[welded] = newVertices.Count;
				newVertices.Add(vertices[welded]);
				newUvs.Add(uvs[welded]);
			}
			return compact[welded];
		}

		internal static void BuildBasis(SweepFrame[] frames, float3[] rights, float3[] ups)
		{
			float3 prevRight = float3.zero;
			float3 prevTangent = float3.zero;
			for (int i = 0; i < frames.Length; i++)
			{
				float3 rawTangent = frames[i].Tangent;
				if (math.lengthsq(rawTangent) < ParallelEpsilon || !math.all(math.isfinite(rawTangent)))
					rawTangent = FallbackTangent(frames, i, prevTangent);

				float3 tangent = math.normalizesafe(rawTangent, new float3(0f, 0f, 1f));
				prevTangent = tangent;
				float3 up = frames[i].Up;
				float3 right = math.normalizesafe(math.cross(up, tangent), new float3(1f, 0f, 0f));
				up = math.cross(tangent, right);

				if (i > 0 && math.dot(right, prevRight) < 0f)
				{
					right = -right;
					up = -up;
				}

				rights[i] = right;
				ups[i] = up;
				prevRight = right;
			}
		}

		private static float3 FallbackTangent(SweepFrame[] frames, int index, float3 prevTangent)
		{
			if (index > 0)
			{
				float3 back = frames[index].Position - frames[index - 1].Position;
				if (math.lengthsq(back) > ParallelEpsilon)
					return back;
			}

			if (index + 1 < frames.Length)
			{
				float3 forward = frames[index + 1].Position - frames[index].Position;
				if (math.lengthsq(forward) > ParallelEpsilon)
					return forward;
			}

			return math.lengthsq(prevTangent) > ParallelEpsilon ? prevTangent : new float3(0f, 0f, 1f);
		}

		private static float SampleLut(float[] lut, float t)
		{
			float f = math.saturate(t) * (lut.Length - 1);
			int i0 = (int)math.floor(f);
			int i1 = math.min(i0 + 1, lut.Length - 1);
			float frac = f - i0;
			return math.lerp(lut[i0], lut[i1], frac);
		}

		internal static List<float2> ExtractOutline(float2[] points, int[] segments)
		{
			var outline = new List<float2>();
			int segPairs = segments.Length / 2;
			for (int s = 0; s < segPairs; s++)
			{
				AppendOutlinePoint(outline, points[segments[s * 2]]);
				AppendOutlinePoint(outline, points[segments[s * 2 + 1]]);
			}

			if (outline.Count > 1 && math.distancesq(outline[outline.Count - 1], outline[0]) < 1e-8f)
				outline.RemoveAt(outline.Count - 1);

			return outline;
		}

		private static void AppendOutlinePoint(List<float2> outline, float2 point)
		{
			if (outline.Count == 0 || math.distancesq(outline[outline.Count - 1], point) > 1e-8f)
				outline.Add(point);
		}

		internal static int[] MapOutlineToProfile(List<float2> outline, float2[] points)
		{
			var map = new int[outline.Count];
			for (int o = 0; o < outline.Count; o++)
			{
				int best = 0;
				float bestDist = float.MaxValue;
				for (int j = 0; j < points.Length; j++)
				{
					float d = math.distancesq(points[j], outline[o]);
					if (d < bestDist)
					{
						bestDist = d;
						best = j;
					}
				}
				map[o] = best;
			}
			return map;
		}

		internal static List<int> Triangulate(List<float2> poly)
		{
			var indices = new List<int>();
			int n = poly.Count;
			if (n < 3)
				return indices;

			var v = new List<int>(n);
			if (SignedArea(poly) < 0f)
			{
				for (int i = n - 1; i >= 0; i--)
					v.Add(i);
			}
			else
			{
				for (int i = 0; i < n; i++)
					v.Add(i);
			}

			int guard = n * n;
			while (v.Count > 3 && guard-- > 0)
			{
				bool clipped = false;
				for (int i = 0; i < v.Count; i++)
				{
					int i0 = v[(i - 1 + v.Count) % v.Count];
					int i1 = v[i];
					int i2 = v[(i + 1) % v.Count];
					float2 a = poly[i0];
					float2 b = poly[i1];
					float2 c = poly[i2];

					if (Cross(b - a, c - a) <= 0f)
						continue;

					bool ear = true;
					for (int j = 0; j < v.Count; j++)
					{
						int vj = v[j];
						if (vj == i0 || vj == i1 || vj == i2)
							continue;

						if (PointInTriangle(poly[vj], a, b, c))
						{
							ear = false;
							break;
						}
					}

					if (!ear)
						continue;

					indices.Add(i0);
					indices.Add(i1);
					indices.Add(i2);
					v.RemoveAt(i);
					clipped = true;
					break;
				}

				if (!clipped)
					break;
			}

			if (v.Count == 3)
			{
				indices.Add(v[0]);
				indices.Add(v[1]);
				indices.Add(v[2]);
			}

			return indices;
		}

		private static float SignedArea(List<float2> poly)
		{
			float area = 0f;
			for (int i = 0; i < poly.Count; i++)
			{
				float2 a = poly[i];
				float2 b = poly[(i + 1) % poly.Count];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}

		private static float Cross(float2 a, float2 b)
		{
			return a.x * b.y - a.y * b.x;
		}

		private static bool PointInTriangle(float2 p, float2 a, float2 b, float2 c)
		{
			float d1 = Cross(b - a, p - a);
			float d2 = Cross(c - b, p - b);
			float d3 = Cross(a - c, p - c);
			bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
			bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
			return !(hasNeg && hasPos);
		}
	}
}
