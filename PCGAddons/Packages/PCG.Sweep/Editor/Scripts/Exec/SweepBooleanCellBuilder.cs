using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal static class SweepBooleanCellBuilder
	{
		private const int PropertyCount = 7;

		internal static bool TryBuild(SweepSnapshot snapshot, SweepBooleanProfile profile, int splineIndex, int cellIndex, float3[] rights, float3[] ups, float3 booleanOrigin, int[] capTriangles, uint keepId, uint discardId, bool keepStartCap, bool keepEndCap, out ManifoldBooleanInput input, out string failure)
		{
			input = null;
			failure = null;
			SweepFrame[] frames = snapshot.Frames[splineIndex];
			if (frames == null || cellIndex < 0 || cellIndex + 1 >= frames.Length)
			{
				failure = "CellRangeInvalid";
				return false;
			}

			int ringSize = profile.Points.Length;
			if (ringSize < 3 || profile.KeepEdges.Length != ringSize || capTriangles == null || capTriangles.Length < 3)
			{
				failure = "CellProfileInvalid";
				return false;
			}

			var ringPositions = new float3[ringSize * 2];
			var ringVertical = new float[ringSize * 2];
			bool hasTerrain = snapshot.Terrain != null;
			for (int ring = 0; ring < 2; ring++)
			{
				int frameIndex = cellIndex + ring;
				SweepFrame frame = frames[frameIndex];
				float3 basePosition = frame.Position - booleanOrigin;
				float width = SweepJunctionMeshBuilder.SampleLut(snapshot.WidthLut, frame.T);
				float height = SweepJunctionMeshBuilder.SampleLut(snapshot.HeightLut, frame.T);
				float twist = math.radians(SweepJunctionMeshBuilder.SampleLut(snapshot.TwistLut, frame.T));
				float cosine = math.cos(twist);
				float sine = math.sin(twist);
				for (int pointIndex = 0; pointIndex < ringSize; pointIndex++)
				{
					float2 point = profile.Points[pointIndex];
					float lateral = point.x * width;
					float vertical = point.y * height;
					float rotatedLateral = lateral * cosine - vertical * sine;
					float rotatedVertical = lateral * sine + vertical * cosine;
					SweepJunctionMeshBuilder.MakeVertex(hasTerrain, basePosition, rights[frameIndex], ups[frameIndex], rotatedLateral, rotatedVertical, out float3 position, out float terrainVertical);
					int index = ring * ringSize + pointIndex;
					ringPositions[index] = position;
					ringVertical[index] = hasTerrain ? terrainVertical : rotatedVertical;
				}
			}

			var properties = new List<float>();
			var mergeFrom = new List<uint>();
			var mergeTo = new List<uint>();
			var canonical = new int[ringSize * 2];
			for (int i = 0; i < canonical.Length; i++)
				canonical[i] = -1;
			var kept = new List<uint>();
			var discarded = new List<uint>();

			uint Vertex(int ring, int pointIndex, float u, float v, bool keepSurface)
			{
				int geometryIndex = ring * ringSize + pointIndex;
				float3 position = ringPositions[geometryIndex];
				uint vertex = (uint)(properties.Count / PropertyCount);
				properties.Add(position.x);
				properties.Add(position.y);
				properties.Add(position.z);
				properties.Add(u);
				properties.Add(v);
				properties.Add(ringVertical[geometryIndex]);
				properties.Add(keepSurface ? 1f : 0f);
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

			float v0 = frames[cellIndex].Distance * snapshot.UvScale;
			float v1 = frames[cellIndex + 1].Distance * snapshot.UvScale;
			for (int edge = 0; edge < ringSize; edge++)
			{
				int next = (edge + 1) % ringSize;
				bool keepSurface = profile.KeepEdges[edge];
				uint a = Vertex(0, edge, profile.EdgeU0[edge], v0, keepSurface);
				uint b = Vertex(0, next, profile.EdgeU1[edge], v0, keepSurface);
				uint c = Vertex(1, edge, profile.EdgeU0[edge], v1, keepSurface);
				uint d = Vertex(1, next, profile.EdgeU1[edge], v1, keepSurface);
				List<uint> target = keepSurface ? kept : discarded;
				Triangle(target, a, c, b);
				Triangle(target, b, c, d);
			}

			bool startExternal = cellIndex == 0;
			bool endExternal = cellIndex == frames.Length - 2;
			bool keepStartSurface = snapshot.ProfileClosed && (!startExternal || keepStartCap);
			bool keepEndSurface = snapshot.ProfileClosed && (!endExternal || keepEndCap);
			for (int triangle = 0; triangle < capTriangles.Length; triangle += 3)
			{
				int p0 = capTriangles[triangle];
				int p1 = capTriangles[triangle + 1];
				int p2 = capTriangles[triangle + 2];
				uint s0 = Vertex(0, p0, profile.Points[p0].x * snapshot.UvScale, profile.Points[p0].y * snapshot.UvScale, keepStartSurface);
				uint s1 = Vertex(0, p1, profile.Points[p1].x * snapshot.UvScale, profile.Points[p1].y * snapshot.UvScale, keepStartSurface);
				uint s2 = Vertex(0, p2, profile.Points[p2].x * snapshot.UvScale, profile.Points[p2].y * snapshot.UvScale, keepStartSurface);
				Triangle(keepStartSurface ? kept : discarded, s0, s2, s1);

				uint e0 = Vertex(1, p0, profile.Points[p0].x * snapshot.UvScale, profile.Points[p0].y * snapshot.UvScale, keepEndSurface);
				uint e1 = Vertex(1, p1, profile.Points[p1].x * snapshot.UvScale, profile.Points[p1].y * snapshot.UvScale, keepEndSurface);
				uint e2 = Vertex(1, p2, profile.Points[p2].x * snapshot.UvScale, profile.Points[p2].y * snapshot.UvScale, keepEndSurface);
				Triangle(keepEndSurface ? kept : discarded, e0, e1, e2);
			}

			var triangles = new List<uint>(kept.Count + discarded.Count);
			triangles.AddRange(kept);
			triangles.AddRange(discarded);
			double signedVolume = SignedVolume(properties, triangles);
			if (triangles.Count < 12 || math.abs(signedVolume) < 1e-10)
			{
				failure = "CellVolumeInvalid";
				return false;
			}
			if (signedVolume < 0.0)
			{
				for (int i = 0; i < triangles.Count; i += 3)
				{
					uint value = triangles[i + 1];
					triangles[i + 1] = triangles[i + 2];
					triangles[i + 2] = value;
				}
			}

			var runIndices = new List<uint>();
			var runIds = new List<uint>();
			runIndices.Add(0);
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
			return true;
		}

		private static double SignedVolume(List<float> properties, List<uint> triangles)
		{
			double volume = 0.0;
			double3 origin = Position(properties, triangles[0]);
			for (int i = 0; i < triangles.Count; i += 3)
			{
				double3 a = (double3)Position(properties, triangles[i]) - origin;
				double3 b = (double3)Position(properties, triangles[i + 1]) - origin;
				double3 c = (double3)Position(properties, triangles[i + 2]) - origin;
				volume += math.dot(a, math.cross(b, c));
			}
			return volume / 6.0;
		}

		private static float3 Position(List<float> properties, uint vertex)
		{
			int index = (int)vertex * PropertyCount;
			return new float3(properties[index], properties[index + 1], properties[index + 2]);
		}
	}
}
