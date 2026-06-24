using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Instances;
using PCG.Utilities;
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

			var region = GetInputValue(nameof(Data.Region), Data.Region);
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

			using (var scope = OperationScope.Start(this))
			{
				var data = RegionMeshBuilder.Build(region, terrain, terrainPosition, maxHeightError, minCellSize, maxCellSize, maxDepth, heightOffset, uvScale);
				Results.Value.Add(new MeshInstanceData
				{
					Name = name,
					Material = material,
					Vertices = data.Vertices,
					Uvs = data.Uvs,
					Triangles = data.Triangles
				});

				await scope.Step(ct: ct);
			}

			var container = InstanceMakerContainer;
			if (container != null && (PcgComputeSystem.IsGenerating || IsPreviewLocal || IsPreviewGlobal))
			{
				await ClearInstancesAsync(ct);

				container.Begin();
				await container.AddInstances(Address.ToKey(), null, Results.Value, ct);
				container.End();
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
