using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepJunctionBooleanMeshBuilder
	{
		private const int MaxArms = 4096;
		private const int MaxVertices = 2000000;

		internal static SweepMeshData Build(SweepNetworkSnapshot snapshot, int junctionIndex, CancellationToken ct, Action reportProgress)
		{
			if (snapshot == null || snapshot.Pieces == null || snapshot.Junctions == null || junctionIndex < 0 || junctionIndex >= snapshot.Junctions.Length)
				return new SweepMeshData { FailureCode = "BooleanJunctionInvalid" };
			SweepNetworkJunction junction = snapshot.Junctions[junctionIndex];
			SweepNetworkArm[] arms = junction?.Arms;
			if (arms == null || arms.Length == 0)
				return new SweepMeshData { FailureCode = "BooleanJunctionArmsEmpty" };
			if (arms.Length > MaxArms)
				return new SweepMeshData { FailureCode = "BooleanJunctionArmBudgetExceeded" };

			SweepSnapshot pieces = snapshot.Pieces;
			float closureThickness = math.max(0.005f, pieces.MaxLateralExtent * 0.02f);
			if (!SweepBooleanProfileBuilder.TryBuild(pieces.ProfilePoints, pieces.ProfileUs, pieces.ProfileSegments, pieces.ProfileClosed, closureThickness, out SweepBooleanProfile profile, out string profileFailure))
				return new SweepMeshData { FailureCode = profileFailure };
			int[] profileSourceIndices = MapProfileSources(profile.Points, pieces.ProfilePoints);
			int[] capTriangles = SweepMeshBuilder.Triangulate(new List<float2>(profile.Points)).ToArray();
			int expectedCapIndices = (profile.Points.Length - 2) * 3;
			if (capTriangles.Length != expectedCapIndices)
				return new SweepMeshData { FailureCode = "BooleanJunctionCapTriangulationFailed" };

			uint firstId;
			try
			{
				firstId = ManifoldBooleanAdapter.ReserveIds(2);
			}
			catch (Exception exception)
			{
				return new SweepMeshData { FailureCode = "BooleanUnavailable-" + exception.GetType().Name };
			}

			var inputs = new List<ManifoldBooleanInput>(arms.Length);
			bool terrainOutOfBounds = false;
			for (int armIndex = 0; armIndex < arms.Length; armIndex++)
			{
				ct.ThrowIfCancellationRequested();
				if (!SweepJunctionBooleanOperandBuilder.TryBuild(
					snapshot,
					junction,
					arms[armIndex],
					armIndex,
					profile,
					profileSourceIndices,
					capTriangles,
					junction.Center,
					firstId,
					firstId + 1,
					ct,
					reportProgress,
					out ManifoldBooleanInput input,
					out bool operandOutOfBounds,
					out string operandFailure))
				{
					return new SweepMeshData { FailureCode = operandFailure + "-a" + armIndex };
				}
				inputs.Add(input);
				terrainOutOfBounds |= operandOutOfBounds;
				reportProgress?.Invoke();
			}

			double booleanTolerance = ComputeTolerance(inputs, junction.Center);
			if (!ManifoldBooleanAdapter.TryUnion(inputs, booleanTolerance, out ManifoldBooleanOutput output, out string booleanFailure))
				return new SweepMeshData { FailureCode = "BooleanJunctionFailed-" + booleanFailure };
			if (output == null || output.Properties == null || output.Triangles == null || output.PropertyCount < 9 || output.RunIndices == null || output.RunOriginalIds == null || output.RunIndices.Length != output.RunOriginalIds.Length + 1)
				return new SweepMeshData { FailureCode = "BooleanJunctionOutputInvalid" };

			int vertexCount = output.Properties.Length / output.PropertyCount;
			if (vertexCount <= 0 || vertexCount > MaxVertices)
				return new SweepMeshData { FailureCode = "BooleanJunctionVertexBudgetExceeded" };
			if (output.MergeFromVertices == null || output.MergeToVertices == null || output.MergeFromVertices.Length != output.MergeToVertices.Length)
				return new SweepMeshData { FailureCode = "BooleanJunctionMergeInvalid" };

			var mergeParents = new int[vertexCount];
			for (int vertex = 0; vertex < vertexCount; vertex++)
				mergeParents[vertex] = vertex;
			for (int merge = 0; merge < output.MergeFromVertices.Length; merge++)
			{
				uint from = output.MergeFromVertices[merge];
				uint to = output.MergeToVertices[merge];
				uint vertexLimit = (uint)vertexCount;
				if (from >= vertexLimit || to >= vertexLimit)
					return new SweepMeshData { FailureCode = "BooleanJunctionMergeIndexInvalid" };
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
			}

			var vertices = new Vector3[vertexCount];
			var uvs = new Vector2[vertexCount];
			int sourceStride = pieces.ProfilePoints.Length;
			for (int vertex = 0; vertex < vertexCount; vertex++)
			{
				int property = vertex * output.PropertyCount;
				float3 position = new float3(output.Properties[property], output.Properties[property + 1], output.Properties[property + 2]) + junction.Center;
				float portalWeight = output.Properties[property + 8];
				float portalCode = output.Properties[property + 7];
				int roundedCode = (int)math.round(portalCode);
				if (portalWeight > 0.9999f && roundedCode > 0 && math.abs(portalCode - roundedCode) < 0.001f)
				{
					int encoded = roundedCode - 1;
					int armIndex = encoded / sourceStride;
					int sourceIndex = encoded % sourceStride;
					if (armIndex >= 0 && armIndex < arms.Length && TryGetCaptured(snapshot, arms[armIndex], sourceIndex, out Vector3 captured))
						position = new float3(captured.x, captured.y, captured.z);
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
			for (int triangle = 0; triangle < output.Triangles.Length; triangle += 3)
			{
				if (!IsVisible(output, triangle, firstId))
					continue;
				uint rawA = output.Triangles[triangle];
				uint rawB = output.Triangles[triangle + 1];
				uint rawC = output.Triangles[triangle + 2];
				uint vertexLimit = (uint)vertexCount;
				if (rawA >= vertexLimit || rawB >= vertexLimit || rawC >= vertexLimit)
					return new SweepMeshData { FailureCode = "BooleanJunctionTriangleInvalid" };
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
				return new SweepMeshData { FailureCode = "BooleanJunctionVisibleSurfaceEmpty" };

			int[] triangleArray = triangles.ToArray();
			CompactReferenced(ref vertices, ref uvs, ref triangleArray, ct);
			if (vertices.Length == 0 || triangleArray.Length == 0)
				return new SweepMeshData { FailureCode = "BooleanJunctionCleanupEmpty" };

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangleArray,
				TerrainOutOfBounds = terrainOutOfBounds
			};
		}

		private static int[] MapProfileSources(float2[] profile, float2[] source)
		{
			var result = new int[profile.Length];
			Array.Fill(result, -1);
			for (int pointIndex = 0; pointIndex < profile.Length; pointIndex++)
			{
				float bestDistance = 1e-10f;
				for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
				{
					float distance = math.distancesq(profile[pointIndex], source[sourceIndex]);
					if (distance > bestDistance)
						continue;
					bestDistance = distance;
					result[pointIndex] = sourceIndex;
				}
			}
			return result;
		}

		private static bool TryGetCaptured(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, int sourceIndex, out Vector3 captured)
		{
			captured = default;
			Vector3[][] rings = arm.AtPieceStart ? snapshot.PieceStartRings : snapshot.PieceEndRings;
			if (rings == null || arm.PieceIndex < 0 || arm.PieceIndex >= rings.Length || rings[arm.PieceIndex] == null || sourceIndex < 0 || sourceIndex >= rings[arm.PieceIndex].Length)
				return false;
			captured = rings[arm.PieceIndex][sourceIndex];
			return true;
		}

		private static double ComputeTolerance(List<ManifoldBooleanInput> inputs, float3 origin)
		{
			float maximumWorldMagnitude = math.cmax(math.abs(origin));
			for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
			{
				ManifoldBooleanInput input = inputs[inputIndex];
				for (int property = 0; property < input.Properties.Length; property += input.PropertyCount)
				{
					float3 local = new float3(input.Properties[property], input.Properties[property + 1], input.Properties[property + 2]);
					maximumWorldMagnitude = math.max(maximumWorldMagnitude, math.cmax(math.abs(local + origin)));
				}
			}
			if (!math.isfinite(maximumWorldMagnitude))
				return 1e-6;
			double magnitude = Math.Max(maximumWorldMagnitude, 1.1754943508222875e-38);
			int exponent = (int)Math.Floor(Math.Log(magnitude, 2.0));
			double ulp = Math.Pow(2.0, exponent - 23);
			return Math.Max(1e-6, ulp * 2.0);
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

		private static void Swap(ref int a, ref int b)
		{
			int value = a;
			a = b;
			b = value;
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
	}
}
