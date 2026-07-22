using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepJunctionBooleanOperandBuilder
	{
		private const int PropertyCount = 9;

		internal static bool TryBuild(
			SweepNetworkSnapshot snapshot,
			SweepNetworkJunction junction,
			SweepNetworkArm arm,
			int armIndex,
			SweepBooleanProfile profile,
			int[] profileSourceIndices,
			int[] capTriangles,
			float3 booleanOrigin,
			uint keepId,
			uint discardId,
			CancellationToken ct,
			Action reportProgress,
			out ManifoldBooleanInput input,
			out bool terrainOutOfBounds,
			out string failure)
		{
			input = null;
			terrainOutOfBounds = false;
			failure = string.Empty;
			bool outOfBounds = false;
			SweepSnapshot pieces = snapshot.Pieces;
			if (pieces == null || profile == null || profile.Points == null || profileSourceIndices == null || profileSourceIndices.Length != profile.Points.Length)
			{
				failure = "OperandProfileInvalid";
				return false;
			}
			if (arm == null || arm.PieceIndex < 0 || pieces.Frames == null || arm.PieceIndex >= pieces.Frames.Length)
			{
				failure = "OperandArmInvalid";
				return false;
			}
			int approachCount = arm.ApproachFrames?.Length ?? 0;
			if (approachCount < 2 || arm.ApproachRights == null || arm.ApproachUps == null || arm.ApproachRights.Length != approachCount || arm.ApproachUps.Length != approachCount)
			{
				failure = "OperandApproachInvalid";
				return false;
			}
			Vector3[][] capturedRings = arm.AtPieceStart ? snapshot.PieceStartRings : snapshot.PieceEndRings;
			if (capturedRings == null || arm.PieceIndex >= capturedRings.Length || capturedRings[arm.PieceIndex] == null)
			{
				failure = "OperandPortalMissing";
				return false;
			}
			Vector3[] capturedRing = capturedRings[arm.PieceIndex];
			for (int pointIndex = 0; pointIndex < profileSourceIndices.Length; pointIndex++)
			{
				int sourceIndex = profileSourceIndices[pointIndex];
				if (sourceIndex >= capturedRing.Length)
				{
					failure = "OperandPortalSizeMismatch";
					return false;
				}
			}

			bool hasTerrain = pieces.Terrain != null;
			int lastApproach = approachCount - 1;
			float directError = PortalError(1f);
			float flippedError = PortalError(-1f);
			float basisDirection = flippedError + 1e-8f < directError ? -1f : 1f;
			int ringSize = profile.Points.Length;
			int ringCount = approachCount + 1;
			var ringPositions = new float3[ringCount * ringSize];
			var ringV = new float[ringCount];
			var portalCodes = new float[ringSize];
			int sourceStride = pieces.ProfilePoints.Length;
			for (int pointIndex = 0; pointIndex < ringSize; pointIndex++)
			{
				int sourceIndex = profileSourceIndices[pointIndex];
				portalCodes[pointIndex] = sourceIndex >= 0 ? armIndex * sourceStride + sourceIndex + 1 : 0f;
			}

			int destinationRing = 0;
			BuildRing(arm.ApproachFrames[lastApproach], arm.ApproachRights[lastApproach], arm.ApproachUps[lastApproach], destinationRing, true);
			ringV[destinationRing] = arm.Frame.Distance * snapshot.UvScale;
			destinationRing++;
			for (int approachIndex = lastApproach - 1; approachIndex >= 1; approachIndex--)
			{
				ct.ThrowIfCancellationRequested();
				SweepFrame frame = arm.ApproachFrames[approachIndex];
				BuildRing(frame, arm.ApproachRights[approachIndex], arm.ApproachUps[approachIndex], destinationRing, false);
				ringV[destinationRing] = frame.Distance * snapshot.UvScale;
				destinationRing++;
				reportProgress?.Invoke();
			}

			SweepFrame centerFrame = arm.ApproachFrames[0];
			centerFrame.Position = junction.Center;
			BuildRing(centerFrame, arm.ApproachRights[0], arm.ApproachUps[0], destinationRing, false);
			ringV[destinationRing] = centerFrame.Distance * snapshot.UvScale;
			destinationRing++;

			float centerExtent = ProfileExtent(centerFrame.T);
			float overlapLimit = math.max(0.01f, snapshot.Step * 0.25f);
			float overlap = math.clamp(centerExtent * 0.02f, 0.01f, overlapLimit);
			SweepFrame deepFrame = centerFrame;
			deepFrame.Position = junction.Center - math.normalizesafe(arm.Outward, -arm.EdgeDir) * overlap;
			BuildRing(deepFrame, arm.ApproachRights[0], arm.ApproachUps[0], destinationRing, false);
			ringV[destinationRing] = centerFrame.Distance * snapshot.UvScale + (arm.AtPieceStart ? -overlap : overlap) * snapshot.UvScale;
			if (destinationRing != ringCount - 1)
			{
				failure = "OperandRingCountInvalid";
				return false;
			}

			var properties = new List<float>(ringCount * ringSize * PropertyCount * 3);
			var mergeFrom = new List<uint>();
			var mergeTo = new List<uint>();
			var canonical = new int[ringCount * ringSize];
			Array.Fill(canonical, -1);
			var kept = new List<uint>();
			var discarded = new List<uint>();

			uint Vertex(int ring, int pointIndex, float u, bool visible)
			{
				int geometryIndex = ring * ringSize + pointIndex;
				float3 position = ringPositions[geometryIndex] - booleanOrigin;
				uint vertex = (uint)(properties.Count / PropertyCount);
				properties.Add(position.x);
				properties.Add(position.y);
				properties.Add(position.z);
				properties.Add(u);
				properties.Add(ringV[ring]);
				properties.Add(0f);
				properties.Add(visible ? 1f : 0f);
				properties.Add(ring == 0 ? portalCodes[pointIndex] : 0f);
				properties.Add(ring == 0 && portalCodes[pointIndex] > 0f ? 1f : 0f);
				if (canonical[geometryIndex] < 0)
				{
					canonical[geometryIndex] = (int)vertex;
				}
				else
				{
					mergeFrom.Add(vertex);
					mergeTo.Add((uint)canonical[geometryIndex]);
				}
				return vertex;
			}

			void Triangle(List<uint> target, uint a, uint b, uint c)
			{
				target.Add(a);
				target.Add(b);
				target.Add(c);
			}

			for (int ring = 0; ring < ringCount - 1; ring++)
			{
				ct.ThrowIfCancellationRequested();
				for (int edge = 0; edge < ringSize; edge++)
				{
					int next = (edge + 1) % ringSize;
					bool visible = profile.KeepEdges[edge];
					uint a = Vertex(ring, edge, profile.EdgeU0[edge], visible);
					uint b = Vertex(ring, next, profile.EdgeU1[edge], visible);
					uint c = Vertex(ring + 1, edge, profile.EdgeU0[edge], visible);
					uint d = Vertex(ring + 1, next, profile.EdgeU1[edge], visible);
					List<uint> target = visible ? kept : discarded;
					Triangle(target, a, c, b);
					Triangle(target, b, c, d);
				}
				reportProgress?.Invoke();
			}

			for (int triangle = 0; triangle < capTriangles.Length; triangle += 3)
			{
				int p0 = capTriangles[triangle];
				int p1 = capTriangles[triangle + 1];
				int p2 = capTriangles[triangle + 2];
				uint s0 = Vertex(0, p0, profile.Points[p0].x * snapshot.UvScale, false);
				uint s1 = Vertex(0, p1, profile.Points[p1].x * snapshot.UvScale, false);
				uint s2 = Vertex(0, p2, profile.Points[p2].x * snapshot.UvScale, false);
				Triangle(discarded, s0, s2, s1);

				int deepRing = ringCount - 1;
				uint e0 = Vertex(deepRing, p0, profile.Points[p0].x * snapshot.UvScale, false);
				uint e1 = Vertex(deepRing, p1, profile.Points[p1].x * snapshot.UvScale, false);
				uint e2 = Vertex(deepRing, p2, profile.Points[p2].x * snapshot.UvScale, false);
				Triangle(discarded, e0, e1, e2);
			}

			var triangles = new List<uint>(kept.Count + discarded.Count);
			triangles.AddRange(kept);
			triangles.AddRange(discarded);
			double signedVolume = SignedVolume(properties, triangles);
			if (triangles.Count < 12 || math.abs(signedVolume) < 1e-10)
			{
				failure = "OperandVolumeInvalid";
				return false;
			}
			if (signedVolume < 0.0)
			{
				for (int triangle = 0; triangle < triangles.Count; triangle += 3)
				{
					uint value = triangles[triangle + 1];
					triangles[triangle + 1] = triangles[triangle + 2];
					triangles[triangle + 2] = value;
				}
			}

			var runIndices = new List<uint> { 0 };
			var runIds = new List<uint>();
			if (kept.Count > 0)
			{
				runIds.Add(keepId);
				runIndices.Add((uint)kept.Count);
			}
			if (discarded.Count > 0)
			{
				runIds.Add(discardId);
				runIndices.Add((uint)triangles.Count);
			}

			input = new ManifoldBooleanInput
			{
				Properties = properties.ToArray(),
				PropertyCount = PropertyCount,
				Triangles = triangles.ToArray(),
				RunIndices = runIndices.ToArray(),
				RunOriginalIds = runIds.ToArray(),
				MergeFromVertices = mergeFrom.ToArray(),
				MergeToVertices = mergeTo.ToArray()
			};
			terrainOutOfBounds = outOfBounds;
			return true;

			float PortalError(float direction)
			{
				float error = 0f;
				int samples = 0;
				for (int pointIndex = 0; pointIndex < profile.Points.Length; pointIndex++)
				{
					int sourceIndex = profileSourceIndices[pointIndex];
					if (sourceIndex < 0)
						continue;
					MakePosition(arm.ApproachFrames[lastApproach], arm.ApproachRights[lastApproach] * direction, arm.ApproachUps[lastApproach] * direction, profile.Points[pointIndex], false, out float3 position);
					Vector3 captured = capturedRing[sourceIndex];
					error += math.distancesq(position, new float3(captured.x, captured.y, captured.z));
					samples++;
				}
				return samples > 0 ? error / samples : 0f;
			}

			void BuildRing(SweepFrame frame, float3 right, float3 up, int ring, bool portal)
			{
				for (int pointIndex = 0; pointIndex < ringSize; pointIndex++)
				{
					int sourceIndex = profileSourceIndices[pointIndex];
					if (portal && sourceIndex >= 0)
					{
						Vector3 captured = capturedRing[sourceIndex];
						ringPositions[ring * ringSize + pointIndex] = new float3(captured.x, captured.y, captured.z);
						continue;
					}
					MakePosition(frame, right * basisDirection, up * basisDirection, profile.Points[pointIndex], true, out float3 position);
					ringPositions[ring * ringSize + pointIndex] = position;
				}
			}

			void MakePosition(SweepFrame frame, float3 right, float3 up, float2 point, bool recordTerrainFailure, out float3 position)
			{
				float width = SweepJunctionMeshBuilder.SampleLut(pieces.WidthLut, frame.T);
				float height = SweepJunctionMeshBuilder.SampleLut(pieces.HeightLut, frame.T);
				float twist = math.radians(SweepJunctionMeshBuilder.SampleLut(pieces.TwistLut, frame.T));
				float lateral = point.x * width;
				float vertical = point.y * height;
				float cosine = math.cos(twist);
				float sine = math.sin(twist);
				float rotatedLateral = lateral * cosine - vertical * sine;
				float rotatedVertical = lateral * sine + vertical * cosine;
				if (!hasTerrain)
				{
					position = frame.Position + right * rotatedLateral + up * rotatedVertical;
					return;
				}
				float2 rightXz = math.normalizesafe(new float2(right.x, right.z), new float2(1f, 0f));
				position = new float3(frame.Position.x + rightXz.x * rotatedLateral, frame.Position.y + rotatedVertical, frame.Position.z + rightXz.y * rotatedLateral);
				if (pieces.Terrain.TrySampleHeight(position.x, position.z, out float terrainHeight))
				{
					position.y = terrainHeight + snapshot.HeightOffset + rotatedVertical;
				}
				else if (recordTerrainFailure)
				{
					outOfBounds = true;
				}
			}

			float ProfileExtent(float t)
			{
				float width = math.abs(SweepJunctionMeshBuilder.SampleLut(pieces.WidthLut, t));
				float height = math.abs(SweepJunctionMeshBuilder.SampleLut(pieces.HeightLut, t));
				float extent = 0f;
				for (int pointIndex = 0; pointIndex < profile.Points.Length; pointIndex++)
				{
					float2 point = profile.Points[pointIndex];
					extent = math.max(extent, math.max(math.abs(point.x) * width, math.abs(point.y) * height));
				}
				return math.max(0.01f, extent);
			}
		}

		private static double SignedVolume(List<float> properties, List<uint> triangles)
		{
			double3 origin = Position(properties, triangles[0]);
			double volume = 0.0;
			for (int triangle = 0; triangle < triangles.Count; triangle += 3)
			{
				double3 a = (double3)Position(properties, triangles[triangle]) - origin;
				double3 b = (double3)Position(properties, triangles[triangle + 1]) - origin;
				double3 c = (double3)Position(properties, triangles[triangle + 2]) - origin;
				volume += math.dot(a, math.cross(b, c));
			}
			return volume / 6.0;
		}

		private static float3 Position(List<float> properties, uint vertex)
		{
			int property = (int)vertex * PropertyCount;
			return new float3(properties[property], properties[property + 1], properties[property + 2]);
		}
	}
}
