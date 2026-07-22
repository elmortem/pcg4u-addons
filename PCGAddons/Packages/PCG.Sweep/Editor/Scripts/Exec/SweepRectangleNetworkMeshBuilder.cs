using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRectangleNetworkMeshBuilder
	{
		private const float MinimumTriangleAreaSquared = 1e-20f;

		internal static bool CanBuild(SweepNetworkSnapshot snapshot, out string failure)
		{
			failure = null;
			if (snapshot == null || snapshot.Pieces == null || snapshot.Pieces.Frames == null || snapshot.Junctions == null)
			{
				failure = "RectangleNetworkDataMissing";
				return false;
			}
			return SweepRectangleProfileInfo.TryCreate(snapshot.Pieces, out _, out failure);
		}

		internal static bool TryBuild(
			SweepNetworkSnapshot snapshot,
			CancellationToken ct,
			Action reportProgress,
			out SweepRibbonNetworkBuildResult result,
			out string failure)
		{
			result = null;
			if (!CanBuild(snapshot, out failure))
				return false;
			if (!SweepRectangleProfileInfo.TryCreate(snapshot.Pieces, out SweepRectangleProfileInfo profile, out failure))
				return false;
			if (!SweepRectangleSurfacePairBuilder.TryBuild(snapshot, profile, ct, reportProgress, out SweepRibbonNetworkDomain domain, out SweepRectangleSourceTriangle[] sources, out failure))
				return false;

			var meshes = new SweepMeshData[domain.Components.Length];
			for (int componentIndex = 0; componentIndex < domain.Components.Length; componentIndex++)
			{
				ct.ThrowIfCancellationRequested();
				SweepRibbonNetworkDomainComponent component = domain.Components[componentIndex];
				if (!SweepRectangleSourceSampler.TryCreate(component.Sources, sources, domain.SourceCellSize, domain.HeightTolerance, out SweepRectangleSourceSampler sampler, out failure))
				{
					failure += "-Component-" + componentIndex;
					return false;
				}
				if (!TryBuildComponent(snapshot, profile, component, sampler, ct, reportProgress, out meshes[componentIndex], out failure))
				{
					failure += "-Component-" + componentIndex;
					return false;
				}
			}

			result = new SweepRibbonNetworkBuildResult
			{
				Domain = domain,
				Meshes = meshes
			};
			return true;
		}

		private static bool TryBuildComponent(
			SweepNetworkSnapshot snapshot,
			SweepRectangleProfileInfo profile,
			SweepRibbonNetworkDomainComponent component,
			SweepRectangleSourceSampler sampler,
			CancellationToken ct,
			Action reportProgress,
			out SweepMeshData mesh,
			out string failure)
		{
			mesh = default;
			failure = null;
			int planVertexCount = component.PlanVertices.Length;
			var bottom = new float3[planVertexCount];
			var top = new float3[planVertexCount];
			var bottomUvs = new float2[planVertexCount];
			var topUvs = new float2[planVertexCount];
			float planTolerance = math.max((float)(8.0 / SweepRibbonPolygonUnion.Scale), profile.Width * 1e-5f);
			float planToleranceSq = planTolerance * planTolerance;
			float thicknessTolerance = math.max(1e-5f, profile.Height * 1e-5f);
			for (int vertex = 0; vertex < planVertexCount; vertex++)
			{
				float2 plan = component.PlanVertices[vertex];
				if (!sampler.TrySample(plan, out bottom[vertex], out top[vertex], out bottomUvs[vertex], out topUvs[vertex], out failure))
				{
					failure += "-Vertex-" + vertex;
					return false;
				}
				if (!ValidateSurfacePair(plan, bottom[vertex], top[vertex], bottomUvs[vertex], topUvs[vertex], planToleranceSq, thicknessTolerance, out failure))
				{
					failure += "-Vertex-" + vertex;
					return false;
				}
				if ((vertex & 1023) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress?.Invoke();
				}
			}

			for (int triangle = 0; triangle < component.Triangles.Length; triangle += 3)
			{
				int a = component.Triangles[triangle];
				int b = component.Triangles[triangle + 1];
				int c = component.Triangles[triangle + 2];
				float2 centroid = (component.PlanVertices[a] + component.PlanVertices[b] + component.PlanVertices[c]) / 3f;
				if (!sampler.TrySample(centroid, out float3 centroidBottom, out float3 centroidTop, out float2 centroidBottomUv, out float2 centroidTopUv, out failure))
				{
					failure += "-Triangle-" + triangle / 3;
					return false;
				}
				if (!ValidateSurfacePair(centroid, centroidBottom, centroidTop, centroidBottomUv, centroidTopUv, planToleranceSq, thicknessTolerance, out failure))
				{
					failure += "-Triangle-" + triangle / 3;
					return false;
				}
				if (math.lengthsq(math.cross(bottom[b] - bottom[a], bottom[c] - bottom[a])) <= MinimumTriangleAreaSquared || math.lengthsq(math.cross(top[b] - top[a], top[c] - top[a])) <= MinimumTriangleAreaSquared)
				{
					failure = "RectangleSurfaceTriangleDegenerate-" + triangle / 3;
					return false;
				}
				if ((triangle & 4095) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress?.Invoke();
				}
			}

			var vertices = new List<Vector3>(planVertexCount * 2 + BoundaryEdgeCount(component) * 4);
			var uvs = new List<Vector2>(vertices.Capacity);
			var triangles = new List<int>(component.Triangles.Length * 2 + BoundaryEdgeCount(component) * 6);
			for (int vertex = 0; vertex < planVertexCount; vertex++)
			{
				vertices.Add(ToVector3(bottom[vertex]));
				uvs.Add(ToVector2(bottomUvs[vertex]));
			}
			for (int vertex = 0; vertex < planVertexCount; vertex++)
			{
				vertices.Add(ToVector3(top[vertex]));
				uvs.Add(ToVector2(topUvs[vertex]));
			}
			for (int triangle = 0; triangle < component.Triangles.Length; triangle += 3)
			{
				int a = component.Triangles[triangle];
				int b = component.Triangles[triangle + 1];
				int c = component.Triangles[triangle + 2];
				triangles.Add(a);
				triangles.Add(b);
				triangles.Add(c);
				triangles.Add(planVertexCount + a);
				triangles.Add(planVertexCount + c);
				triangles.Add(planVertexCount + b);
			}

			int boundaryOffset = 0;
			AddWalls(component.Polygon.Outer, component.OuterEdgeKinds, boundaryOffset, snapshot.CapEnds, bottom, top, bottomUvs, topUvs, vertices, uvs, triangles);
			boundaryOffset += component.Polygon.Outer.Length;
			for (int hole = 0; hole < component.Polygon.Holes.Count; hole++)
			{
				AddWalls(component.Polygon.Holes[hole], component.HoleEdgeKinds[hole], boundaryOffset, snapshot.CapEnds, bottom, top, bottomUvs, topUvs, vertices, uvs, triangles);
				boundaryOffset += component.Polygon.Holes[hole].Length;
			}

			var vertexArray = vertices.ToArray();
			var uvArray = uvs.ToArray();
			var triangleArray = triangles.ToArray();
			if (!ValidateMesh(vertexArray, uvArray, triangleArray, snapshot.CapEnds, out failure))
				return false;
			mesh = new SweepMeshData
			{
				Vertices = vertexArray,
				Uvs = uvArray,
				Triangles = triangleArray,
				TerrainOutOfBounds = component.TerrainOutOfBounds
			};
			return true;
		}

		private static bool ValidateSurfacePair(
			float2 plan,
			float3 bottom,
			float3 top,
			float2 bottomUv,
			float2 topUv,
			float planToleranceSq,
			float thicknessTolerance,
			out string failure)
		{
			failure = null;
			if (!math.all(math.isfinite(bottom)) || !math.all(math.isfinite(top)) || !math.all(math.isfinite(bottomUv)) || !math.all(math.isfinite(topUv)))
			{
				failure = "RectangleSurfaceSampleInvalid";
				return false;
			}
			float2 bottomPlan = new float2(bottom.x, bottom.z);
			float2 topPlan = new float2(top.x, top.z);
			if (math.distancesq(bottomPlan, plan) > planToleranceSq)
			{
				failure = "RectangleBottomPlanMismatch";
				return false;
			}
			if (math.distancesq(topPlan, plan) > planToleranceSq)
			{
				failure = "RectangleTopNotHeightfield";
				return false;
			}
			if (top.y - bottom.y <= thicknessTolerance)
			{
				failure = "RectangleThicknessInvalid";
				return false;
			}
			return true;
		}

		private static void AddWalls(
			float2[] ring,
			SweepRibbonBoundaryKind[] kinds,
			int boundaryOffset,
			bool capEnds,
			float3[] bottom,
			float3[] top,
			float2[] bottomUvs,
			float2[] topUvs,
			List<Vector3> vertices,
			List<Vector2> uvs,
			List<int> triangles)
		{
			for (int edge = 0; edge < ring.Length; edge++)
			{
				if (kinds[edge] == SweepRibbonBoundaryKind.Terminal && !capEnds)
					continue;
				int a = boundaryOffset + edge;
				int b = boundaryOffset + (edge + 1) % ring.Length;
				int start = vertices.Count;
				vertices.Add(ToVector3(bottom[a]));
				vertices.Add(ToVector3(bottom[b]));
				vertices.Add(ToVector3(top[a]));
				vertices.Add(ToVector3(top[b]));
				uvs.Add(ToVector2(bottomUvs[a]));
				uvs.Add(ToVector2(bottomUvs[b]));
				uvs.Add(ToVector2(topUvs[a]));
				uvs.Add(ToVector2(topUvs[b]));
				triangles.Add(start);
				triangles.Add(start + 2);
				triangles.Add(start + 1);
				triangles.Add(start + 1);
				triangles.Add(start + 2);
				triangles.Add(start + 3);
			}
		}

		private static int BoundaryEdgeCount(SweepRibbonNetworkDomainComponent component)
		{
			int count = component.Polygon.Outer.Length;
			for (int hole = 0; hole < component.Polygon.Holes.Count; hole++)
				count += component.Polygon.Holes[hole].Length;
			return count;
		}

		private static bool ValidateMesh(Vector3[] vertices, Vector2[] uvs, int[] triangles, bool closed, out string failure)
		{
			failure = null;
			if (vertices.Length == 0 || vertices.Length != uvs.Length || triangles.Length == 0 || triangles.Length % 3 != 0)
			{
				failure = "RectangleMeshEmpty";
				return false;
			}
			var welded = new int[vertices.Length];
			var positions = new Dictionary<(long, long, long), int>();
			for (int vertex = 0; vertex < vertices.Length; vertex++)
			{
				Vector3 value = vertices[vertex];
				Vector2 uv = uvs[vertex];
				if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z) || !float.IsFinite(uv.x) || !float.IsFinite(uv.y))
				{
					failure = "RectangleMeshVertexInvalid-" + vertex;
					return false;
				}
				var key = ((long)math.round((double)value.x * SweepRibbonPolygonUnion.Scale), (long)math.round((double)value.y * SweepRibbonPolygonUnion.Scale), (long)math.round((double)value.z * SweepRibbonPolygonUnion.Scale));
				if (!positions.TryGetValue(key, out int index))
				{
					index = positions.Count;
					positions.Add(key, index);
				}
				welded[vertex] = index;
			}

			var edgeCounts = new Dictionary<ulong, int>();
			var edgeBalances = new Dictionary<ulong, int>();
			var triangleKeys = new HashSet<(int, int, int)>();
			for (int triangle = 0; triangle < triangles.Length; triangle += 3)
			{
				int ia = triangles[triangle];
				int ib = triangles[triangle + 1];
				int ic = triangles[triangle + 2];
				if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length || ia == ib || ib == ic || ia == ic)
				{
					failure = "RectangleMeshIndexInvalid-" + triangle / 3;
					return false;
				}
				float3 a = ToFloat3(vertices[ia]);
				float3 b = ToFloat3(vertices[ib]);
				float3 c = ToFloat3(vertices[ic]);
				if (math.lengthsq(math.cross(b - a, c - a)) <= MinimumTriangleAreaSquared)
				{
					failure = "RectangleMeshTriangleDegenerate-" + triangle / 3;
					return false;
				}
				int wa = welded[ia];
				int wb = welded[ib];
				int wc = welded[ic];
				if (wa == wb || wb == wc || wa == wc)
				{
					failure = "RectangleMeshWeldedDegenerate-" + triangle / 3;
					return false;
				}
				Sort(ref wa, ref wb, ref wc);
				if (!triangleKeys.Add((wa, wb, wc)))
				{
					failure = "RectangleMeshDuplicateTriangle-" + triangle / 3;
					return false;
				}
				CountEdge(edgeCounts, edgeBalances, welded[ia], welded[ib]);
				CountEdge(edgeCounts, edgeBalances, welded[ib], welded[ic]);
				CountEdge(edgeCounts, edgeBalances, welded[ic], welded[ia]);
			}

			var boundaryDegrees = new Dictionary<int, int>();
			foreach (var pair in edgeCounts)
			{
				if (pair.Value < 1 || pair.Value > 2)
				{
					failure = "RectangleMeshNonManifoldEdge";
					return false;
				}
				if (pair.Value == 2 && edgeBalances[pair.Key] != 0)
				{
					failure = "RectangleMeshWindingMismatch";
					return false;
				}
				if (pair.Value == 2)
					continue;
				if (closed)
				{
					failure = "RectangleMeshOpenEdge";
					return false;
				}
				int a = (int)(pair.Key >> 32);
				int b = (int)(pair.Key & uint.MaxValue);
				Increment(boundaryDegrees, a);
				Increment(boundaryDegrees, b);
			}
			foreach (var pair in boundaryDegrees)
			{
				if (pair.Value != 2)
				{
					failure = "RectangleMeshBoundaryBranch";
					return false;
				}
			}
			return true;
		}

		private static void CountEdge(Dictionary<ulong, int> counts, Dictionary<ulong, int> balances, int first, int second)
		{
			uint minimum = (uint)math.min(first, second);
			uint maximum = (uint)math.max(first, second);
			ulong key = ((ulong)minimum << 32) | maximum;
			counts.TryGetValue(key, out int count);
			counts[key] = count + 1;
			balances.TryGetValue(key, out int balance);
			balances[key] = balance + (first < second ? 1 : -1);
		}

		private static void Increment(Dictionary<int, int> counts, int key)
		{
			counts.TryGetValue(key, out int count);
			counts[key] = count + 1;
		}

		private static void Sort(ref int a, ref int b, ref int c)
		{
			if (a > b)
				Swap(ref a, ref b);
			if (b > c)
				Swap(ref b, ref c);
			if (a > b)
				Swap(ref a, ref b);
		}

		private static void Swap(ref int first, ref int second)
		{
			int value = first;
			first = second;
			second = value;
		}

		private static Vector3 ToVector3(float3 value)
		{
			return new Vector3(value.x, value.y, value.z);
		}

		private static Vector2 ToVector2(float2 value)
		{
			return new Vector2(value.x, value.y);
		}

		private static float3 ToFloat3(Vector3 value)
		{
			return new float3(value.x, value.y, value.z);
		}
	}
}
