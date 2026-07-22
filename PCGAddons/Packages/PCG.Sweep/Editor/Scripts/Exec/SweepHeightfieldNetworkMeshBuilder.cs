using System;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepHeightfieldNetworkMeshBuilder
	{
		private const float MinimumTriangleAreaSquared = 1e-20f;

		internal static bool CanBuild(SweepNetworkSnapshot snapshot, out string failure)
		{
			if (!SweepRibbonNetworkDomainBuilder.CanBuildHeightfield(snapshot, out failure))
				return false;
			return TryGetProfileProperties(snapshot.Pieces, out _, out _, out failure);
		}

		internal static bool TryBuild(SweepNetworkSnapshot snapshot, CancellationToken ct, Action reportProgress, out SweepRibbonNetworkBuildResult result, out string failure)
		{
			result = null;
			if (!TryGetProfileProperties(snapshot.Pieces, out bool minimumEnvelope, out bool positiveWinding, out failure))
				return false;
			if (!SweepRibbonNetworkDomainBuilder.TryBuildHeightfield(snapshot, ct, reportProgress, out SweepRibbonNetworkDomain domain, out failure))
				return false;

			var meshes = new SweepMeshData[domain.Components.Length];
			for (int componentIndex = 0; componentIndex < domain.Components.Length; componentIndex++)
			{
				ct.ThrowIfCancellationRequested();
				SweepRibbonNetworkDomainComponent component = domain.Components[componentIndex];
				var sampler = new SweepRibbonSourceSampler(component.Sources, math.max(0.05f, snapshot.Step), domain.HeightTolerance, true, minimumEnvelope);
				var vertices = new Vector3[component.PlanVertices.Length];
				var uvs = new Vector2[component.PlanVertices.Length];
				for (int vertex = 0; vertex < component.PlanVertices.Length; vertex++)
				{
					float2 plan = component.PlanVertices[vertex];
					if (!sampler.TrySample(plan, out float height, out float2 uv, out failure) || !math.isfinite(height) || !math.all(math.isfinite(uv)))
					{
						failure = (failure ?? "HeightfieldSampleInvalid") + "-Component-" + componentIndex + "-Vertex-" + vertex;
						return false;
					}
					vertices[vertex] = new Vector3(plan.x, height, plan.y);
					uvs[vertex] = new Vector2(uv.x, uv.y);
					if ((vertex & 1023) == 0)
					{
						ct.ThrowIfCancellationRequested();
						reportProgress?.Invoke();
					}
				}

				var triangles = new int[component.Triangles.Length];
				for (int triangle = 0; triangle < component.Triangles.Length; triangle += 3)
				{
					int a = component.Triangles[triangle];
					int b = component.Triangles[triangle + 1];
					int c = component.Triangles[triangle + 2];
					float2 centroid = (component.PlanVertices[a] + component.PlanVertices[b] + component.PlanVertices[c]) / 3f;
					if (!sampler.TrySample(centroid, out _, out _, out failure))
					{
						failure += "-Component-" + componentIndex + "-Triangle-" + triangle / 3;
						return false;
					}
					if (math.lengthsq(math.cross(ToFloat3(vertices[b] - vertices[a]), ToFloat3(vertices[c] - vertices[a]))) <= MinimumTriangleAreaSquared)
					{
						failure = "HeightfieldTriangleDegenerate-Component-" + componentIndex + "-Triangle-" + triangle / 3;
						return false;
					}
					triangles[triangle] = a;
					triangles[triangle + 1] = positiveWinding ? c : b;
					triangles[triangle + 2] = positiveWinding ? b : c;
					if ((triangle & 4095) == 0)
					{
						ct.ThrowIfCancellationRequested();
						reportProgress?.Invoke();
					}
				}

				meshes[componentIndex] = new SweepMeshData
				{
					Vertices = vertices,
					Uvs = uvs,
					Triangles = triangles,
					TerrainOutOfBounds = component.TerrainOutOfBounds
				};
			}

			result = new SweepRibbonNetworkBuildResult
			{
				Domain = domain,
				Meshes = meshes
			};
			return true;
		}

		private static bool TryGetProfileProperties(SweepSnapshot snapshot, out bool minimumEnvelope, out bool positiveWinding, out string failure)
		{
			minimumEnvelope = true;
			positiveWinding = true;
			failure = null;
			if (snapshot == null || snapshot.ProfilePoints == null || snapshot.ProfileSegments == null || snapshot.ProfileSegments.Length < 4)
			{
				failure = "HeightfieldProfileMissing";
				return false;
			}
			int first = snapshot.ProfileSegments[0];
			int last = snapshot.ProfileSegments[snapshot.ProfileSegments.Length - 1];
			float direction = snapshot.ProfilePoints[last].x - snapshot.ProfilePoints[first].x;
			if (!math.isfinite(direction) || math.abs(direction) <= 1e-6f)
			{
				failure = "HeightfieldProfileDirectionInvalid";
				return false;
			}
			float area = 0f;
			for (int edge = 0; edge < snapshot.ProfileSegments.Length; edge += 2)
			{
				float2 a = snapshot.ProfilePoints[snapshot.ProfileSegments[edge]];
				float2 b = snapshot.ProfilePoints[snapshot.ProfileSegments[edge + 1]];
				area += a.x * b.y - b.x * a.y;
			}
			float2 end = snapshot.ProfilePoints[last];
			float2 start = snapshot.ProfilePoints[first];
			area = (area + end.x * start.y - start.x * end.y) * 0.5f;
			if (!math.isfinite(area) || math.abs(area) <= 1e-8f)
			{
				failure = "HeightfieldProfileAreaInvalid";
				return false;
			}
			positiveWinding = direction > 0f;
			minimumEnvelope = area * math.sign(direction) > 0f;
			return true;
		}

		private static float3 ToFloat3(Vector3 value)
		{
			return new float3(value.x, value.y, value.z);
		}
	}
}
