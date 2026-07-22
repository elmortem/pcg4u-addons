using System;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRibbonNetworkMeshBuilder
	{
		internal static bool CanBuild(SweepNetworkSnapshot snapshot, out string failure)
		{
			return SweepRibbonNetworkDomainBuilder.CanBuild(snapshot, out failure);
		}

		internal static bool TryBuild(SweepNetworkSnapshot snapshot, CancellationToken ct, Action reportProgress, out SweepRibbonNetworkBuildResult result, out string failure)
		{
			result = null;
			if (!SweepRibbonNetworkDomainBuilder.TryBuild(snapshot, ct, reportProgress, out SweepRibbonNetworkDomain domain, out failure))
				return false;

			var meshes = new SweepMeshData[domain.Components.Length];
			for (int componentIndex = 0; componentIndex < domain.Components.Length; componentIndex++)
			{
				ct.ThrowIfCancellationRequested();
				SweepRibbonNetworkDomainComponent component = domain.Components[componentIndex];
				var sampler = new SweepRibbonSourceSampler(component.Sources, domain.SourceCellSize, domain.HeightTolerance);
				var vertices = new Vector3[component.PlanVertices.Length];
				var uvs = new Vector2[component.PlanVertices.Length];
				for (int vertex = 0; vertex < component.PlanVertices.Length; vertex++)
				{
					float2 plan = component.PlanVertices[vertex];
					if (!sampler.TrySample(plan, out float height, out float2 uv, out failure))
					{
						failure += "-Component-" + componentIndex + "-Vertex-" + vertex;
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
					triangles[triangle] = a;
					triangles[triangle + 1] = c;
					triangles[triangle + 2] = b;
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
	}
}
