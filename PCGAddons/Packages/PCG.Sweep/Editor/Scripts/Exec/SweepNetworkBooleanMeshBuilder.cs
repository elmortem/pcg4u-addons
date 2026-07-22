using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Splines;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepNetworkBooleanMeshBuilder
	{
		private const int MaxCells = 100000;
		private const int MaxVertices = 2000000;
		internal static int DiagnosticBuildCount;
		internal static string DiagnosticTopology = string.Empty;

		internal static SweepMeshData Build(SweepSnapshot snapshot, SplineNetworkTopology topology, CancellationToken ct, Action reportProgress)
		{
			int diagnosticBuild = Interlocked.Increment(ref DiagnosticBuildCount);
			DiagnosticTopology = "started-" + diagnosticBuild;
			float closureThickness = math.max(0.005f, snapshot.MaxLateralExtent * 0.02f);
			if (!SweepBooleanProfileBuilder.TryBuild(snapshot.ProfilePoints, snapshot.ProfileUs, snapshot.ProfileSegments, snapshot.ProfileClosed, closureThickness, out SweepBooleanProfile profile, out string profileFailure))
				return new SweepMeshData { FailureCode = profileFailure };

			var capOutline = new List<float2>(profile.Points);
			int[] capTriangles = SweepMeshBuilder.Triangulate(capOutline).ToArray();
			if (capTriangles.Length < 3)
				return new SweepMeshData { FailureCode = "BooleanCapTriangulationFailed" };

			uint firstId;
			try
			{
				firstId = ManifoldBooleanAdapter.ReserveIds(2);
			}
			catch (Exception exception)
			{
				return new SweepMeshData { FailureCode = "BooleanUnavailable-" + exception.GetType().Name };
			}
			uint keepId = firstId;
			uint discardId = firstId + 1;
			var inputs = new List<ManifoldBooleanInput>();
			int operations = 0;
			float3 boundsMinimum = new float3(float.MaxValue);
			float3 boundsMaximum = new float3(float.MinValue);
			for (int splineIndex = 0; splineIndex < snapshot.Frames.Length; splineIndex++)
			{
				SweepFrame[] frames = snapshot.Frames[splineIndex];
				for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
				{
					boundsMinimum = math.min(boundsMinimum, frames[frameIndex].Position);
					boundsMaximum = math.max(boundsMaximum, frames[frameIndex].Position);
				}
			}
			float3 booleanOrigin = (boundsMinimum + boundsMaximum) * 0.5f;

			for (int splineIndex = 0; splineIndex < snapshot.Frames.Length; splineIndex++)
			{
				SweepFrame[] frames = snapshot.Frames[splineIndex];
				if (frames == null || frames.Length < 2)
					continue;
				var rights = new float3[frames.Length];
				var ups = new float3[frames.Length];
				SweepMeshBuilder.BuildBasis(frames, rights, ups);
				bool closed = snapshot.SplineClosed[splineIndex];
				if (closed)
				{
					rights[rights.Length - 1] = rights[0];
					ups[ups.Length - 1] = ups[0];
				}

				bool keepStartCap = snapshot.ProfileClosed && !closed && snapshot.CapStartFlags[splineIndex];
				bool keepEndCap = snapshot.ProfileClosed && !closed && snapshot.CapEndFlags[splineIndex];
				for (int cellIndex = 0; cellIndex < frames.Length - 1; cellIndex++)
				{
					ct.ThrowIfCancellationRequested();
					if (inputs.Count >= MaxCells)
						return new SweepMeshData { FailureCode = "BooleanCellBudgetExceeded" };
					if (!SweepBooleanCellBuilder.TryBuild(snapshot, profile, splineIndex, cellIndex, rights, ups, booleanOrigin, capTriangles, keepId, discardId, keepStartCap, keepEndCap, out ManifoldBooleanInput input, out string cellFailure))
						return new SweepMeshData { FailureCode = cellFailure + "-s" + splineIndex + "-c" + cellIndex };
					inputs.Add(input);
					operations++;
					if ((operations & 127) == 0)
						reportProgress?.Invoke();
				}
			}

			if (inputs.Count == 0)
				return new SweepMeshData { FailureCode = "BooleanCellsEmpty" };
			float coordinateScale = snapshot.MaxLateralExtent;
			for (int splineIndex = 0; splineIndex < snapshot.Frames.Length; splineIndex++)
			{
				SweepFrame[] frames = snapshot.Frames[splineIndex];
				for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
					coordinateScale = math.max(coordinateScale, math.cmax(math.abs(frames[frameIndex].Position - booleanOrigin)));
			}
			double simplifyTolerance = math.max(1e-5f, coordinateScale * 5e-7f);
			if (!ManifoldBooleanAdapter.TryUnion(inputs, simplifyTolerance, out ManifoldBooleanOutput output, out string booleanFailure))
				return new SweepMeshData { FailureCode = "BooleanFailed-" + booleanFailure };
			if (output == null || output.Properties == null || output.Triangles == null || output.PropertyCount < 7 || output.RunIndices == null || output.RunOriginalIds == null || output.RunIndices.Length != output.RunOriginalIds.Length + 1)
				return new SweepMeshData { FailureCode = "BooleanOutputInvalid" };
			AnalyzeTopology(output, null, out int allBoundary, out int allNonManifold, out int allDuplicate);
			AnalyzeTopology(output, keepId, out int keepBoundary, out int keepNonManifold, out int keepDuplicate);
			DiagnosticTopology = diagnosticBuild + ":all=" + allBoundary + "/" + allNonManifold + "/" + allDuplicate + ":keep=" + keepBoundary + "/" + keepNonManifold + "/" + keepDuplicate + ":merges=" + output.MergeFromVertices.Length + ":runs=" + output.RunOriginalIds.Length + ":ids=" + UniqueIds(output.RunOriginalIds) + ":keepId=" + keepId + ":discardId=" + discardId;

			int vertexCount = output.Properties.Length / output.PropertyCount;
			if (vertexCount <= 0 || vertexCount > MaxVertices)
				return new SweepMeshData { FailureCode = "BooleanVertexBudgetExceeded" };
			if (output.MergeFromVertices == null || output.MergeToVertices == null || output.MergeFromVertices.Length != output.MergeToVertices.Length)
				return new SweepMeshData { FailureCode = "BooleanMergeInvalid" };
			var mergeParents = new int[vertexCount];
			for (int vertex = 0; vertex < vertexCount; vertex++)
				mergeParents[vertex] = vertex;
			for (int merge = 0; merge < output.MergeFromVertices.Length; merge++)
			{
				uint from = output.MergeFromVertices[merge];
				uint to = output.MergeToVertices[merge];
				uint vertexLimit = (uint)vertexCount;
				if (from >= vertexLimit || to >= vertexLimit)
					return new SweepMeshData { FailureCode = "BooleanMergeIndexInvalid" };
				mergeParents[(int)from] = (int)to;
			}
			int MergeRoot(int vertex)
			{
				int root = vertex;
				while (mergeParents[root] != root)
					root = mergeParents[root];
				while (mergeParents[vertex] != vertex)
				{
					int next = mergeParents[vertex];
					mergeParents[vertex] = root;
					vertex = next;
				}
				return root;
			}
			for (int vertex = 0; vertex < vertexCount; vertex++)
			{
				int root = MergeRoot(vertex);
				if (root == vertex)
					continue;
				int property = vertex * output.PropertyCount;
				int rootProperty = root * output.PropertyCount;
				output.Properties[property] = output.Properties[rootProperty];
				output.Properties[property + 1] = output.Properties[rootProperty + 1];
				output.Properties[property + 2] = output.Properties[rootProperty + 2];
				output.Properties[property + 5] = output.Properties[rootProperty + 5];
			}
			var vertices = new Vector3[vertexCount];
			var uvs = new Vector2[vertexCount];
			bool outOfBounds = false;
			for (int vertex = 0; vertex < vertexCount; vertex++)
			{
				int property = vertex * output.PropertyCount;
				float3 position = new float3(output.Properties[property], output.Properties[property + 1], output.Properties[property + 2]) + booleanOrigin;
				if (snapshot.Terrain != null)
				{
					if (snapshot.Terrain.TrySampleHeight(position.x, position.z, out float terrainHeight))
						position.y = terrainHeight + snapshot.HeightOffset + output.Properties[property + 5];
					else
						outOfBounds = true;
				}
				vertices[vertex] = new Vector3(position.x, position.y, position.z);
				uvs[vertex] = new Vector2(output.Properties[property + 3], output.Properties[property + 4]);
				if ((vertex & 1023) == 0)
				{
					ct.ThrowIfCancellationRequested();
					reportProgress?.Invoke();
				}
			}

			var triangles = new List<int>();
			var faces = new HashSet<(int, int, int)>();
			for (int index = 0; index < output.Triangles.Length; index += 3)
			{
				if (!IsVisible(output, index, keepId))
					continue;
				uint rawA = output.Triangles[index];
				uint rawB = output.Triangles[index + 1];
				uint rawC = output.Triangles[index + 2];
				uint vertexLimit = (uint)vertexCount;
				if (rawA >= vertexLimit || rawB >= vertexLimit || rawC >= vertexLimit)
					return new SweepMeshData { FailureCode = "BooleanTriangleInvalid" };
				int a = MergeRoot((int)rawA);
				int b = MergeRoot((int)rawB);
				int c = MergeRoot((int)rawC);
				if (a == b || b == c || a == c)
					continue;
				int x = a;
				int y = b;
				int z = c;
				if (x > y)
					Swap(ref x, ref y);
				if (y > z)
					Swap(ref y, ref z);
				if (x > y)
					Swap(ref x, ref y);
				if (!faces.Add((x, y, z)))
					continue;
				triangles.Add(a);
				triangles.Add(c);
				triangles.Add(b);
			}

			if (triangles.Count == 0)
				return new SweepMeshData { FailureCode = "BooleanKeepSurfaceEmpty" };
			int[] triangleArray = triangles.ToArray();
			CompactReferenced(ref vertices, ref uvs, ref triangleArray, ct);
			if (vertices.Length == 0 || triangleArray.Length == 0)
				return new SweepMeshData { FailureCode = "BooleanCleanupEmpty" };
			bool requireClosed = RequiresClosedSurface(snapshot);
			if (!SweepSurfaceTopologyValidator.TryValidate(vertices, uvs, triangleArray, requireClosed, out string topologyFailure))
				return new SweepMeshData { FailureCode = "Boolean" + topologyFailure };

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangleArray,
				TerrainOutOfBounds = outOfBounds
			};
		}

		private static bool RequiresClosedSurface(SweepSnapshot snapshot)
		{
			if (!snapshot.ProfileClosed)
				return false;
			for (int spline = 0; spline < snapshot.Frames.Length; spline++)
			{
				if (snapshot.Frames[spline] == null || snapshot.SplineClosed[spline])
					continue;
				if (!snapshot.CapStartFlags[spline] || !snapshot.CapEndFlags[spline])
					return false;
			}
			return true;
		}

		private static string UniqueIds(uint[] values)
		{
			var found = new HashSet<uint>();
			for (int index = 0; index < values.Length; index++)
				found.Add(values[index]);
			var unique = new uint[found.Count];
			found.CopyTo(unique);
			Array.Sort(unique);
			return string.Join(",", unique);
		}

		private static void CompactReferenced(ref Vector3[] vertices, ref Vector2[] uvs, ref int[] triangles, CancellationToken ct)
		{
			var remap = new int[vertices.Length];
			Array.Fill(remap, -1);
			var compactVertices = new List<Vector3>();
			var compactUvs = new List<Vector2>();
			for (int index = 0; index < triangles.Length; index++)
			{
				int source = triangles[index];
				int destination = remap[source];
				if (destination < 0)
				{
					destination = compactVertices.Count;
					remap[source] = destination;
					compactVertices.Add(vertices[source]);
					compactUvs.Add(uvs[source]);
				}
				triangles[index] = destination;
				if ((index & 4095) == 0)
					ct.ThrowIfCancellationRequested();
			}
			vertices = compactVertices.ToArray();
			uvs = compactUvs.ToArray();
		}

		private static bool IsVisible(ManifoldBooleanOutput output, int triangleIndex, uint keepId)
		{
			int low = 0;
			int high = output.RunOriginalIds.Length - 1;
			while (low <= high)
			{
				int run = low + (high - low) / 2;
				int start = (int)output.RunIndices[run];
				int end = (int)output.RunIndices[run + 1];
				if (triangleIndex < start)
				{
					high = run - 1;
					continue;
				}
				if (triangleIndex >= end)
				{
					low = run + 1;
					continue;
				}
				return output.RunOriginalIds[run] == keepId;
			}
			return false;
		}

		private static void AnalyzeTopology(ManifoldBooleanOutput output, uint? runId, out int boundary, out int nonManifold, out int duplicate)
		{
			int vertexCount = output.Properties.Length / output.PropertyCount;
			var parent = new int[vertexCount];
			for (int i = 0; i < parent.Length; i++)
				parent[i] = i;
			for (int i = 0; i < output.MergeFromVertices.Length; i++)
				parent[(int)output.MergeFromVertices[i]] = (int)output.MergeToVertices[i];
			int Root(int value)
			{
				while (parent[value] != value)
				{
					parent[value] = parent[parent[value]];
					value = parent[value];
				}
				return value;
			}
			var edges = new Dictionary<ulong, int>();
			var faces = new HashSet<string>();
			for (int run = 0; run < output.RunOriginalIds.Length; run++)
			{
				int start = (int)output.RunIndices[run];
				int end = (int)output.RunIndices[run + 1];
				for (int i = start; i < end; i += 3)
				{
					if (runId.HasValue && output.RunOriginalIds[run] != runId.Value)
						continue;
					int a = Root((int)output.Triangles[i]);
					int b = Root((int)output.Triangles[i + 1]);
					int c = Root((int)output.Triangles[i + 2]);
					int x = a;
					int y = b;
					int z = c;
					if (x > y)
						Swap(ref x, ref y);
					if (y > z)
						Swap(ref y, ref z);
					if (x > y)
						Swap(ref x, ref y);
					faces.Add(x + ":" + y + ":" + z);
					CountEdge(edges, a, b);
					CountEdge(edges, b, c);
					CountEdge(edges, c, a);
				}
			}
			int triangleCount = 0;
			foreach (int value in edges.Values)
				triangleCount += value;
			triangleCount /= 3;
			duplicate = triangleCount - faces.Count;
			boundary = 0;
			nonManifold = 0;
			foreach (int count in edges.Values)
			{
				if (count == 1)
					boundary++;
				else if (count > 2)
					nonManifold++;
			}
		}

		private static void CountEdge(Dictionary<ulong, int> edges, int a, int b)
		{
			uint min = (uint)math.min(a, b);
			uint max = (uint)math.max(a, b);
			ulong key = ((ulong)min << 32) | max;
			edges.TryGetValue(key, out int count);
			edges[key] = count + 1;
		}

		private static void Swap(ref int a, ref int b)
		{
			int value = a;
			a = b;
			b = value;
		}
	}
}
