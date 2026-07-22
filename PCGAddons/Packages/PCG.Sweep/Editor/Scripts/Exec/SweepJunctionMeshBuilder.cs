using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepJunctionMeshBuilder
	{
		private const int MaxVertices = 2_000_000;

		internal static SweepMeshData Build(SweepNetworkSnapshot snapshot, int junctionIndex, CancellationToken ct, Action reportProgress)
		{
			var pieces = snapshot.Pieces;
			var junction = snapshot.Junctions[junctionIndex];

			float3 center = junction.Center;
			float3 axis = junction.Axis;
			float3 e1 = junction.E1;
			float3 e2 = junction.E2;
			var arms = junction.Arms;
			int n = arms.Length;
			if (n == 0)
				return default;

			var profile = pieces.ProfilePoints;
			var us = pieces.ProfileUs;
			var segments = pieces.ProfileSegments;
			int vpr = profile.Length;

			float uvScale = snapshot.UvScale;
			float step = math.max(0.05f, snapshot.Step);
			float heightOffset = snapshot.HeightOffset;
			var terrain = pieces.Terrain;
			bool hasTerrain = terrain != null;

			var widthLut = pieces.WidthLut;
			var heightLut = pieces.HeightLut;
			var twistLut = pieces.TwistLut;

			var decomp = SweepProfileDecomposition.Build(profile, segments, pieces.ProfileClosed);

			int maxIndex = 0;
			int minIndex = 0;
			for (int i = 1; i < vpr; i++)
			{
				if (profile[i].x > profile[maxIndex].x)
					maxIndex = i;
				if (profile[i].x < profile[minIndex].x)
					minIndex = i;
			}

			var armWidthMul = new float[n];
			var armHeightMul = new float[n];
			var armTwistCos = new float[n];
			var armTwistSin = new float[n];
			var armCcwIsMax = new bool[n];
			var approachFlip = new bool[n];

			for (int k = 0; k < n; k++)
			{
				var arm = arms[k];
				float tCut = arm.Frame.T;
				float widthMul = SampleLut(widthLut, tCut);
				float heightMul = SampleLut(heightLut, tCut);
				float twist = math.radians(SampleLut(twistLut, tCut));
				armWidthMul[k] = widthMul;
				armHeightMul[k] = heightMul;
				armTwistCos[k] = math.cos(twist);
				armTwistSin[k] = math.sin(twist);
			}

			for (int k = 0; k < n; k++)
			{
				if (armTwistCos[k] <= 1e-3f)
					return new SweepMeshData { FailureCode = "DegenerateChains" };
			}

			var vertices = new List<float3>();
			var uvs = new List<Vector2>();
			var ry = new List<float>();
			var triangles = new List<int>();

			int progress = 0;

			void ProgressTick()
			{
				progress++;
				if ((progress & 1023) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress?.Invoke();
				}
			}

			void Ring(int k, int j, out float3 pos, out float rv)
			{
				LoftEdgeVertex(arms[k], j, profile, armWidthMul[k], armHeightMul[k], armTwistCos[k], armTwistSin[k], hasTerrain, out pos, out rv);
				int pieceIndex = arms[k].PieceIndex;
				Vector3[][] rings = arms[k].AtPieceStart ? snapshot.PieceStartRings : snapshot.PieceEndRings;
				if (rings == null || pieceIndex < 0 || pieceIndex >= rings.Length)
					return;
				Vector3[] ring = rings[pieceIndex];
				if (ring == null || j < 0 || j >= ring.Length)
					return;
				Vector3 captured = ring[j];
				pos = new float3(captured.x, captured.y, captured.z);
			}

			void ApproachRing(int k, int sample, int j, out float3 pos)
			{
				SweepFrame frame = arms[k].ApproachFrames[sample];
				float widthMul = SampleLut(widthLut, frame.T);
				float heightMul = SampleLut(heightLut, frame.T);
				float twist = math.radians(SampleLut(twistLut, frame.T));
				float lateral = profile[j].x * widthMul;
				float vertical = profile[j].y * heightMul;
				float cosine = math.cos(twist);
				float sine = math.sin(twist);
				float rotatedLateral = lateral * cosine - vertical * sine;
				float rotatedVertical = lateral * sine + vertical * cosine;
				float direction = approachFlip[k] ? -1f : 1f;
				MakeVertex(hasTerrain, frame.Position, arms[k].ApproachRights[sample] * direction, arms[k].ApproachUps[sample] * direction, rotatedLateral, rotatedVertical, out pos, out _);
			}

			for (int k = 0; k < n; k++)
			{
				Ring(k, maxIndex, out float3 capturedMax, out _);
				Ring(k, minIndex, out float3 capturedMin, out _);
				LoftEdgeVertex(arms[k], maxIndex, profile, armWidthMul[k], armHeightMul[k], armTwistCos[k], armTwistSin[k], hasTerrain, out float3 rawMax, out _);
				LoftEdgeVertex(arms[k], minIndex, profile, armWidthMul[k], armHeightMul[k], armTwistCos[k], armTwistSin[k], hasTerrain, out float3 rawMin, out _);
				float direct = math.distancesq(capturedMax, rawMax) + math.distancesq(capturedMin, rawMin);
				float flipped = math.distancesq(capturedMax, rawMin) + math.distancesq(capturedMin, rawMax);
				approachFlip[k] = flipped + 1e-8f < direct;
				float2 pMax = Planar(capturedMax - center, e1, e2);
				float deltaMax = NormalizeSigned(math.atan2(pMax.y, pMax.x) - arms[k].Azimuth);
				armCcwIsMax[k] = deltaMax > 0f;
			}

			var portalCw = new float2[n];
			var portalCcw = new float2[n];
			var corridorCw = new float2[n][];
			var corridorCcw = new float2[n][];
			float maxPortalDelta = 0f;
			float minArmRadius = float.MaxValue;
			float maxArmRadius = 0f;
			for (int k = 0; k < n; k++)
			{
				int cwReference = armCcwIsMax[k] ? minIndex : maxIndex;
				int ccwReference = armCcwIsMax[k] ? maxIndex : minIndex;
				Ring(k, cwReference, out float3 cwPosition, out _);
				Ring(k, ccwReference, out float3 ccwPosition, out _);
				LoftEdgeVertex(arms[k], cwReference, profile, armWidthMul[k], armHeightMul[k], armTwistCos[k], armTwistSin[k], hasTerrain, out float3 rawCw, out _);
				LoftEdgeVertex(arms[k], ccwReference, profile, armWidthMul[k], armHeightMul[k], armTwistCos[k], armTwistSin[k], hasTerrain, out float3 rawCcw, out _);
				maxPortalDelta = math.max(maxPortalDelta, math.max(math.distance(cwPosition, rawCw), math.distance(ccwPosition, rawCcw)));
				portalCw[k] = Planar(cwPosition - center, e1, e2);
				portalCcw[k] = Planar(ccwPosition - center, e1, e2);
				float armRadius = math.length(Planar(arms[k].Frame.Position - center, e1, e2));
				minArmRadius = math.min(minArmRadius, armRadius);
				maxArmRadius = math.max(maxArmRadius, armRadius);

				int samples = arms[k].ApproachFrames?.Length ?? 0;
				if (samples < 2 || arms[k].ApproachRights == null || arms[k].ApproachUps == null || arms[k].ApproachRights.Length != samples || arms[k].ApproachUps.Length != samples)
					return new SweepMeshData { FailureCode = "ApproachMissing" };
				corridorCw[k] = new float2[samples];
				corridorCcw[k] = new float2[samples];
				for (int s = 0; s < samples; s++)
				{
					ApproachRing(k, s, cwReference, out float3 cwSample);
					ApproachRing(k, s, ccwReference, out float3 ccwSample);
					corridorCw[k][s] = Planar(cwSample - center, e1, e2);
					corridorCcw[k][s] = Planar(ccwSample - center, e1, e2);
				}
				corridorCw[k][samples - 1] = portalCw[k];
				corridorCcw[k][samples - 1] = portalCcw[k];
			}

			if (!SweepJunctionFootprint.TryBuild(arms, portalCw, portalCcw, corridorCw, corridorCcw, step, out var footprint, out string footprintFailure))
				return new SweepMeshData { FailureCode = $"PortalLayoutInvalid-{footprintFailure}-delta{maxPortalDelta:F3}-n{n}-r{minArmRadius:F2}-{maxArmRadius:F2}" };

			float junctionRadius = 0f;
			for (int k = 0; k < n; k++)
			{
				for (int j = 0; j < vpr; j++)
				{
					Ring(k, j, out float3 rp, out _);
					float2 pl = Planar(rp - center, e1, e2);
					junctionRadius = math.max(junctionRadius, math.length(pl));
				}
			}
			float h = math.max(step, junctionRadius / 24f);

			double area = math.PI * (double)junctionRadius * junctionRadius;
			double estimate = decomp.Chains.Count * (area / (h * (double)h)) + (double)decomp.Walls.Count * n * vpr + n * vpr;
			if (estimate > MaxVertices)
				return new SweepMeshData { FailureCode = "BudgetExceeded" };

			void SampleRim(int k, int slotA, int slotB, out float3[] pos, out float2[] plan, out float[] rv)
			{
				int kb = (k + 1) % n;
				var gap = footprint.Gaps[k];
				int m = gap.Plan.Length;
				Ring(k, slotA, out float3 ae, out float rvA);
				Ring(kb, slotB, out float3 be, out float rvB);
				float2 planA = Planar(ae - center, e1, e2);
				float2 planB = Planar(be - center, e1, e2);
				float hA = math.dot(ae - center, axis);
				float hB = math.dot(be - center, axis);

				pos = new float3[m];
				plan = new float2[m];
				rv = new float[m];
				for (int s = 0; s < m; s++)
				{
					if (s == 0)
					{
						pos[0] = ae;
						plan[0] = planA;
						rv[0] = rvA;
						continue;
					}
					if (s == m - 1)
					{
						pos[m - 1] = be;
						plan[m - 1] = planB;
						rv[m - 1] = rvB;
						continue;
					}

					float t = gap.T[s];
					float2 p2 = gap.Transform(s, planA, planB);
					plan[s] = p2;
					pos[s] = center + p2.x * e1 + p2.y * e2 + axis * math.lerp(hA, hB, t);
					rv[s] = math.lerp(rvA, rvB, t);
				}
			}

			string patchFail = null;

			bool BuildChainSheet(SweepProfileDecomposition.Chain chain)
			{
				var ccwEnd = new int[n];
				var cwEnd = new int[n];
				var traversal = new int[n][];
				int first = chain.Points[0];
				int last = chain.Points[chain.Points.Length - 1];
				for (int k = 0; k < n; k++)
				{
					if (armCcwIsMax[k])
					{
						cwEnd[k] = first;
						ccwEnd[k] = last;
						traversal[k] = chain.Points;
					}
					else
					{
						cwEnd[k] = last;
						ccwEnd[k] = first;
						traversal[k] = Reversed(chain.Points);
					}
				}

				var loopPlan = new List<float2>();
				var loopPos = new List<float3>();
				var loopRv = new List<float>();
				for (int k = 0; k < n; k++)
				{
					int kb = (k + 1) % n;

					int[] chain2 = traversal[k];
					for (int c = 0; c < chain2.Length; c++)
					{
						Ring(k, chain2[c], out float3 p, out float rvv);
						float2 pplan = Planar(p - center, e1, e2);
						AddLoopVertex(loopPlan, loopPos, loopRv, pplan, p, rvv);
					}

					SampleRim(k, ccwEnd[k], cwEnd[kb], out float3[] rpos, out float2[] rplan, out float[] rrv);
					for (int s = 1; s < rplan.Length - 1; s++)
						AddLoopVertex(loopPlan, loopPos, loopRv, rplan[s], rpos[s], rrv[s]);
				}

				if (loopPlan.Count > 1 && math.distancesq(loopPlan[loopPlan.Count - 1], loopPlan[0]) < 1e-10f)
				{
					int lastIdx = loopPlan.Count - 1;
					loopPlan.RemoveAt(lastIdx);
					loopPos.RemoveAt(lastIdx);
					loopRv.RemoveAt(lastIdx);
				}

				int boundaryCount = loopPlan.Count;
				if (boundaryCount < 3)
					return true;

				if (!IsSimple(loopPlan))
				{
					FindTouch(loopPlan, out int touchA, out int touchB);
					patchFail = "DomainNotSimple-" + chain.Class + "-" + loopPlan.Count + "-" + SignedArea(loopPlan).ToString("F3") + "-" + touchA + "-" + touchB + "-n" + n + "-r" + minArmRadius.ToString("F2") + "-" + maxArmRadius.ToString("F2");
					return false;
				}

				if (!SweepJunctionTriangulator.Triangulate(loopPlan, h, ct, reportProgress, out var verts2d, out var tris, out string triangulationFailure) || tris.Count == 0)
				{
					patchFail = $"TriangulationFailed-{triangulationFailure}";
					return false;
				}

				var heightB = new List<float>(boundaryCount);
				for (int i = 0; i < boundaryCount; i++)
					heightB.Add(math.dot(loopPos[i] - center, axis));

				var interp = new SweepJunctionInterpolator(loopPlan, heightB, loopRv);

				int baseIdx = vertices.Count;
				for (int i = 0; i < verts2d.Count; i++)
				{
					float2 v = verts2d[i];
					float3 pos;
					float rv;
					if (i < boundaryCount)
					{
						pos = loopPos[i];
						rv = loopRv[i];
					}
					else
					{
						interp.Sample(v, out float hM, out float rvM);
						pos = center + v.x * e1 + v.y * e2 + axis * hM;
						rv = rvM;
					}

					vertices.Add(pos);
					ry.Add(rv);
					uvs.Add(PlanarUv(pos, center, e1, e2, uvScale));
					ProgressTick();
				}

				bool reverse = chain.Class < 0;
				for (int t = 0; t < tris.Count; t += 3)
				{
					if (reverse)
					{
						triangles.Add(baseIdx + tris[t]);
						triangles.Add(baseIdx + tris[t + 2]);
						triangles.Add(baseIdx + tris[t + 1]);
					}
					else
					{
						triangles.Add(baseIdx + tris[t]);
						triangles.Add(baseIdx + tris[t + 1]);
						triangles.Add(baseIdx + tris[t + 2]);
					}
					ProgressTick();
				}

				return true;
			}

			for (int i = 0; i < decomp.Chains.Count; i++)
			{
				if (!BuildChainSheet(decomp.Chains[i]))
					return new SweepMeshData { FailureCode = patchFail ?? "TriangulationFailed" };
			}

			for (int w = 0; w < decomp.Walls.Count; w++)
			{
				int[] column = decomp.Walls[w].Points;
				if (column == null || column.Length < 2)
					continue;

				for (int k = 0; k < n; k++)
				{
					int cc = column.Length;
					int m = footprint.Gaps[k].Plan.Length;

					SampleRim(k, column[0], column[0], out _, out float2[] plan0, out _);
					var arcLen0 = new float[m];
					for (int s = 1; s < m; s++)
						arcLen0[s] = arcLen0[s - 1] + math.distance(plan0[s - 1], plan0[s]);

					var grid = new int[m, cc];
					for (int e = 0; e < cc; e++)
					{
						int slot = column[e];
						SampleRim(k, slot, slot, out float3[] rpos, out _, out float[] rrv);
						float u = us[slot];
						for (int s = 0; s < m; s++)
						{
							grid[s, e] = vertices.Count;
							vertices.Add(rpos[s]);
							ry.Add(rrv[s]);
							uvs.Add(new Vector2(u, arcLen0[s] * uvScale));
							ProgressTick();
						}
					}

					for (int s = 0; s < m - 1; s++)
					{
						for (int e = 0; e < cc - 1; e++)
						{
							int i0 = grid[s, e];
							int i1 = grid[s + 1, e];
							int i2 = grid[s + 1, e + 1];
							int i3 = grid[s, e + 1];
							float3 normal = math.cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
							float3 radial = (vertices[i0] + vertices[i1] + vertices[i2] + vertices[i3]) * 0.25f - center;
							radial -= math.dot(radial, axis) * axis;
							bool reverse = math.dot(normal, radial) < 0f;
							if (reverse)
							{
								triangles.Add(i0);
								triangles.Add(i2);
								triangles.Add(i1);
								triangles.Add(i0);
								triangles.Add(i3);
								triangles.Add(i2);
							}
							else
							{
								triangles.Add(i0);
								triangles.Add(i1);
								triangles.Add(i2);
								triangles.Add(i0);
								triangles.Add(i2);
								triangles.Add(i3);
							}
						}
					}
				}
			}

			if (decomp.Closed && snapshot.CapEnds)
			{
				var capOutline = SweepMeshBuilder.ExtractOutline(profile, segments);
				if (capOutline.Count >= 3)
				{
					var capOutlineIdx = SweepMeshBuilder.MapOutlineToProfile(capOutline, profile);
					var capTris = SweepMeshBuilder.Triangulate(capOutline);
					for (int k = 0; k < n; k++)
					{
						if (!arms[k].Terminal)
							continue;

						int baseIdx = vertices.Count;
						for (int o = 0; o < capOutline.Count; o++)
						{
							int profileIndex = capOutlineIdx[o];
							Ring(k, profileIndex, out float3 cp, out float crv);
							vertices.Add(cp);
							ry.Add(crv);
							float2 uv = profile[profileIndex] * uvScale;
							uvs.Add(new Vector2(uv.x, uv.y));
							ProgressTick();
						}

						bool reverse = CapReverse(vertices, capTris, baseIdx, arms[k].Outward);
						for (int c = 0; c < capTris.Count; c += 3)
						{
							int o0 = capTris[c];
							int o1 = capTris[c + 1];
							int o2 = capTris[c + 2];
							if (reverse)
							{
								triangles.Add(baseIdx + o0);
								triangles.Add(baseIdx + o2);
								triangles.Add(baseIdx + o1);
							}
							else
							{
								triangles.Add(baseIdx + o0);
								triangles.Add(baseIdx + o1);
								triangles.Add(baseIdx + o2);
							}
							ProgressTick();
						}
					}
				}
			}

			bool outOfBounds = false;
			if (hasTerrain)
			{
				for (int v = 0; v < vertices.Count; v++)
				{
					float3 p = vertices[v];
					if (terrain.TrySampleHeight(p.x, p.z, out float th))
					{
						p.y = th + heightOffset + ry[v];
						vertices[v] = p;
					}
					else
					{
						outOfBounds = true;
					}

					ProgressTick();
				}
			}

			int vertexCount = vertices.Count;
			var vertexArray = new Vector3[vertexCount];
			for (int v = 0; v < vertexCount; v++)
			{
				float3 p = vertices[v];
				vertexArray[v] = new Vector3(p.x, p.y, p.z);
			}

			var uvArray = uvs.ToArray();
			var triangleArray = triangles.ToArray();

			SweepMeshBuilder.Cleanup(ref vertexArray, ref uvArray, ref triangleArray, ct);

			return new SweepMeshData
			{
				Vertices = vertexArray,
				Uvs = uvArray,
				Triangles = triangleArray,
				TerrainOutOfBounds = outOfBounds
			};
		}

		private static bool IsSimple(List<float2> loop)
		{
			int m = loop.Count;
			if (m < 3)
				return false;

			for (int i = 0; i < m; i++)
			{
				float2 a0 = loop[i];
				float2 a1 = loop[(i + 1) % m];
				if (!math.all(math.isfinite(a0)) || math.distancesq(a0, a1) < 1e-12f)
					return false;
				for (int j = i + 1; j < m; j++)
				{
					if (j == i || j == (i + 1) % m || (j + 1) % m == i)
						continue;
					float2 b0 = loop[j];
					float2 b1 = loop[(j + 1) % m];
					if (SegmentsTouch(a0, a1, b0, b1))
						return false;
				}
			}
			return math.abs(SignedArea(loop)) > 1e-8f;
		}

		private static bool FindTouch(List<float2> loop, out int edgeA, out int edgeB)
		{
			edgeA = -1;
			edgeB = -1;
			int count = loop.Count;
			for (int i = 0; i < count; i++)
			{
				float2 a0 = loop[i];
				float2 a1 = loop[(i + 1) % count];
				for (int j = i + 1; j < count; j++)
				{
					if (j == i || j == (i + 1) % count || (j + 1) % count == i)
						continue;
					if (!SegmentsTouch(a0, a1, loop[j], loop[(j + 1) % count]))
						continue;
					edgeA = i;
					edgeB = j;
					return true;
				}
			}
			return false;
		}

		private static bool SegmentsTouch(float2 a0, float2 a1, float2 b0, float2 b1)
		{
			float scale = math.max(1f, math.max(math.length(a1 - a0), math.length(b1 - b0)));
			float epsilon = 1e-7f * scale;
			float d1 = Orient(a0, a1, b0);
			float d2 = Orient(a0, a1, b1);
			float d3 = Orient(b0, b1, a0);
			float d4 = Orient(b0, b1, a1);
			if (((d1 > epsilon && d2 < -epsilon) || (d1 < -epsilon && d2 > epsilon)) &&
				((d3 > epsilon && d4 < -epsilon) || (d3 < -epsilon && d4 > epsilon)))
				return true;
			if (math.abs(d1) <= epsilon && PointOnSegment(b0, a0, a1, epsilon))
				return true;
			if (math.abs(d2) <= epsilon && PointOnSegment(b1, a0, a1, epsilon))
				return true;
			if (math.abs(d3) <= epsilon && PointOnSegment(a0, b0, b1, epsilon))
				return true;
			return math.abs(d4) <= epsilon && PointOnSegment(a1, b0, b1, epsilon);
		}

		private static bool PointOnSegment(float2 point, float2 a, float2 b, float epsilon)
		{
			return point.x >= math.min(a.x, b.x) - epsilon && point.x <= math.max(a.x, b.x) + epsilon &&
				point.y >= math.min(a.y, b.y) - epsilon && point.y <= math.max(a.y, b.y) + epsilon;
		}

		private static float SignedArea(List<float2> loop)
		{
			float area = 0f;
			for (int i = 0; i < loop.Count; i++)
			{
				float2 a = loop[i];
				float2 b = loop[(i + 1) % loop.Count];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}

		private static float Orient(float2 a, float2 b, float2 c)
		{
			return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
		}

		private static void AddLoopVertex(List<float2> plan, List<float3> pos, List<float> rv, float2 planPoint, float3 posPoint, float rvPoint)
		{
			if (plan.Count > 0 && math.distancesq(plan[plan.Count - 1], planPoint) < 1e-10f)
				return;

			plan.Add(planPoint);
			pos.Add(posPoint);
			rv.Add(rvPoint);
		}

		private static bool CapReverse(List<float3> vertices, List<int> tris, int baseIdx, float3 outward)
		{
			for (int c = 0; c < tris.Count; c += 3)
			{
				float3 v0 = vertices[baseIdx + tris[c]];
				float3 v1 = vertices[baseIdx + tris[c + 1]];
				float3 v2 = vertices[baseIdx + tris[c + 2]];
				float3 nrm = math.cross(v1 - v0, v2 - v0);
				if (math.lengthsq(nrm) < 1e-12f)
					continue;
				return math.dot(nrm, outward) < 0f;
			}
			return false;
		}

		private static int[] Reversed(int[] source)
		{
			int len = source.Length;
			var result = new int[len];
			for (int i = 0; i < len; i++)
				result[i] = source[len - 1 - i];
			return result;
		}

		internal static void MakeVertex(bool hasTerrain, float3 basePos, float3 right, float3 up, float lateral, float vertical, out float3 pos, out float ry)
		{
			if (!hasTerrain)
			{
				pos = basePos + right * lateral + up * vertical;
				ry = 0f;
				return;
			}

			float2 rightXz = math.normalizesafe(new float2(right.x, right.z), new float2(1f, 0f));
			pos = new float3(basePos.x + rightXz.x * lateral, basePos.y + vertical, basePos.z + rightXz.y * lateral);
			ry = vertical;
		}

		internal static float SampleLut(float[] lut, float t)
		{
			float f = math.saturate(t) * (lut.Length - 1);
			int i0 = (int)math.floor(f);
			int i1 = math.min(i0 + 1, lut.Length - 1);
			float frac = f - i0;
			return math.lerp(lut[i0], lut[i1], frac);
		}

		private static void LoftEdgeVertex(SweepNetworkArm arm, int colIndex, float2[] profile, float widthMul, float heightMul, float twistCos, float twistSin, bool hasTerrain, out float3 pos, out float ry)
		{
			float lat = profile[colIndex].x * widthMul;
			float vert = profile[colIndex].y * heightMul;
			float rx = lat * twistCos - vert * twistSin;
			float rry = lat * twistSin + vert * twistCos;
			MakeVertex(hasTerrain, arm.Frame.Position, arm.Right, arm.Up, rx, rry, out pos, out ry);
		}

		private static bool CcwIsMax(SweepNetworkArm arm, float3 center, float3 e1, float3 e2, float2[] profile, int maxIndex, int minIndex, float widthMul, float heightMul, float twistCos, float twistSin, bool hasTerrain)
		{
			float3 vMax = CornerVertex(arm, profile[maxIndex], widthMul, heightMul, twistCos, twistSin, hasTerrain);
			float2 pMax = Planar(vMax - center, e1, e2);
			float aMax = math.atan2(pMax.y, pMax.x);
			float deltaMax = NormalizeSigned(aMax - arm.Azimuth);
			return deltaMax > 0f;
		}

		private static float3 CornerVertex(SweepNetworkArm arm, float2 point, float widthMul, float heightMul, float twistCos, float twistSin, bool hasTerrain)
		{
			float lat = point.x * widthMul;
			float vert = point.y * heightMul;
			float rx = lat * twistCos - vert * twistSin;
			float rry = lat * twistSin + vert * twistCos;
			MakeVertex(hasTerrain, arm.Frame.Position, arm.Right, arm.Up, rx, rry, out float3 pos, out _);
			return pos;
		}

		private static Vector2 PlanarUv(float3 pos, float3 center, float3 e1, float3 e2, float uvScale)
		{
			return new Vector2(math.dot(pos - center, e1) * uvScale, math.dot(pos - center, e2) * uvScale);
		}

		private static float2 Planar(float3 rel, float3 e1, float3 e2)
		{
			return new float2(math.dot(rel, e1), math.dot(rel, e2));
		}

		private static float NormalizeSigned(float angle)
		{
			while (angle > math.PI)
				angle -= 2f * math.PI;
			while (angle <= -math.PI)
				angle += 2f * math.PI;
			return angle;
		}
	}
}
