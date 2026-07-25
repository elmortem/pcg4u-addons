using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Instances;
using PCG.Splines;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class RegionToMeshNodeExecutor : PcgAsyncPreviewNodeExecutor<RegionToMeshNode>, INodeInfo, IInstancesNode
	{
		public PcgOutput<List<MeshInstanceData>> Results;

		private IInstanceMakerContainer InstanceMakerContainer => Graph?.Host as IInstanceMakerContainer;

		public override bool IsEmpty => Results.Value == null;
		public bool HasNodeInfo => !IsEmpty && (IsComputed || IsComputing);
		public string NodeInfo => $"Triangles: {TriangleCount()}";

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<MeshInstanceData>();

			if (!Data.Enabled)
			{
				await ClearInstancesAsync(ct);
				return;
			}

			var region = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (region == null || region.Count <= 0)
				return;

			var terrain = GetInputValue(nameof(Data.Terrain), Data.Terrain);
			var terrainPosition = GetInputValue(nameof(Data.Offset), Data.Offset);
			var maxHeightError = GetInputValue(nameof(Data.MaxHeightError), Data.MaxHeightError);
			var minCellSize = GetInputValue(nameof(Data.MinCellSize), Data.MinCellSize);
			var maxCellSize = GetInputValue(nameof(Data.MaxCellSize), Data.MaxCellSize);
			var maxDepth = GetInputValue(nameof(Data.MaxDepth), Data.MaxDepth);
			var heightOffset = GetInputValue(nameof(Data.HeightOffset), Data.HeightOffset);
			var uvScale = GetInputValue(nameof(Data.UvScale), Data.UvScale);
			var name = GetInputValue(nameof(Data.Name), Data.Name);
			var material = GetInputValue(nameof(Data.Material), Data.Material);
			bool collider = Data.Collider;

			Func<float2, float> heightSampler = null;
			if (terrain != null && maxCellSize > 0f)
			{
				GetBounds(region, out float2 min, out float2 max);
				var window = SplineTerrainWindow.Capture(terrain, terrainPosition, min.x, max.x, min.y, max.y);
				float planeY = region.PlaneY;
				heightSampler = p => window.TrySampleHeight(p.x, p.y, out float sampled) ? sampled : planeY;
			}

			var data = await PcgWorkerScheduler.RunAsync(
				() => RegionMeshBuilder.BuildFromHeightSampler(
					region,
					heightSampler,
					maxHeightError,
					minCellSize,
					maxCellSize,
					maxDepth,
					heightOffset,
					uvScale),
				ct);

			Results.Value.Add(new MeshInstanceData
			{
				Name = name,
				Material = material,
				Collider = collider,
				Vertices = data.Vertices,
				Uvs = data.Uvs,
				Triangles = data.Triangles
			});

			var container = InstanceMakerContainer;
			if (container != null && (PcgComputeSystem.IsGenerating || IsPreviewLocal || IsPreviewGlobal))
			{
				await ClearInstancesAsync(ct);

				container.Begin();
				try
				{
					await container.AddInstances(Address.ToKey(), null, Results.Value, ct);
				}
				finally
				{
					container.End();
				}
			}
		}

		private static void GetBounds(RegionSet region, out float2 min, out float2 max)
		{
			min = new float2(float.MaxValue, float.MaxValue);
			max = new float2(float.MinValue, float.MinValue);
			for (int i = 0; i < region.Regions.Count; i++)
			{
				region.Regions[i].GetBounds(out float2 lo, out float2 hi);
				min = math.min(min, lo);
				max = math.max(max, hi);
			}
		}

		public async UniTask ClearInstancesAsync(CancellationToken ct = default)
		{
			var container = InstanceMakerContainer;
			if (container != null && container.HasOwnedObjects(Address.ToKey()))
				await container.RemoveInstances(Address.ToKey(), ct);
			LastComputedVersion = 0;
		}

		public override void DrawPreview(Transform transform)
		{
		}

		private int TriangleCount()
		{
			if (Results.Value == null || Results.Value.Count <= 0)
				return 0;

			var triangles = Results.Value[0].Triangles;
			return triangles == null ? 0 : triangles.Length / 3;
		}
	}
}
