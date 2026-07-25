using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Instances;
using PCG.Polygons.City;
using PCG.Splines;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public sealed class RegionExtrudeNodeExecutor : PcgAsyncPreviewNodeExecutor<RegionExtrudeNode>, INodeInfo, IInstancesNode
	{
		public PcgOutput<List<MeshInstanceData>> Results;

		private IInstanceMakerContainer InstanceMakerContainer => Graph?.Host as IInstanceMakerContainer;

		public override bool IsEmpty => Results.Value == null || Results.Value.Count == 0;
		public bool HasNodeInfo => !IsEmpty && (IsComputed || IsComputing);
		public string NodeInfo => $"Meshes: {Results.Value?.Count ?? 0}, Triangles: {TriangleCount()}";

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<MeshInstanceData>();
			if (!Data.Enabled)
			{
				await ClearInstancesAsync(ct);
				return;
			}

			var region = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (region == null || region.Count == 0)
			{
				await ClearInstancesAsync(ct);
				return;
			}

			float baseOffset = GetInputValue(nameof(Data.BaseOffset), Data.BaseOffset);
			float height = math.max(0.001f, GetInputValue(nameof(Data.Height), Data.Height));
			float uvScale = GetInputValue(nameof(Data.UvScale), Data.UvScale);
			string name = GetInputValue(nameof(Data.Name), Data.Name);
			Material topMaterial = GetInputValue(nameof(Data.TopMaterial), Data.TopMaterial);
			Material sideMaterial = GetInputValue(nameof(Data.SideMaterial), Data.SideMaterial);
			if (sideMaterial == null)
				sideMaterial = topMaterial;
			TerrainData terrain = GetInputValue(nameof(Data.Terrain), Data.Terrain);
			Vector3 terrainOffset = GetInputValue(nameof(Data.TerrainOffset), Data.TerrainOffset);
			var terrainMode = Data.TerrainMode;
			bool collider = Data.Collider;

			Func<float2, float> heightSampler = null;
			if (terrain != null && terrainMode != RegionExtrudeTerrainMode.Planar)
			{
				GetBounds(region, out float2 min, out float2 max);
				var window = SplineTerrainWindow.Capture(terrain, terrainOffset, min.x, max.x, min.y, max.y);
				float planeY = region.PlaneY;
				heightSampler = point => window.TrySampleHeight(point.x, point.y, out float sampled) ? sampled : planeY;
			}

			var buildTask = RunOnLargeStack(
				() => RegionExtrudeBuilder.BuildFromHeightSampler(
					region,
					heightSampler,
					terrainMode,
					baseOffset,
					height,
					uvScale,
					name,
					topMaterial,
					sideMaterial,
					collider,
					ct),
				ct);
			while (buildTask.Status == UniTaskStatus.Pending)
			{
				PcgComputeSystem.ReportProgress(this);
				await UniTask.Delay(250, cancellationToken: ct);
			}
			var meshes = await buildTask;
			PcgComputeSystem.ReportProgress(this);

			Results.Value = meshes;
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

		private static UniTask<T> RunOnLargeStack<T>(Func<T> work, CancellationToken ct)
		{
			var completion = new UniTaskCompletionSource<T>();
			var thread = new Thread(
				() =>
				{
					try
					{
						ct.ThrowIfCancellationRequested();
						completion.TrySetResult(work());
					}
					catch (Exception exception)
					{
						completion.TrySetException(exception);
					}
				},
				16 * 1024 * 1024)
			{
				IsBackground = true,
				Name = "PCG Region Extrude"
			};
			thread.Start();
			return completion.Task;
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
			if (Results.Value == null)
				return 0;

			int count = 0;
			for (int i = 0; i < Results.Value.Count; i++)
			{
				var triangles = Results.Value[i].Triangles;
				if (triangles != null)
					count += triangles.Length / 3;
			}
			return count;
		}
	}
}
