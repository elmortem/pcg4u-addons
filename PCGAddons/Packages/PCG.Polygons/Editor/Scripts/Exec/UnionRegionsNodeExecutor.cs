using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using UnityEngine;

namespace PCG.Polygons.City
{
	public sealed class UnionRegionsNodeExecutor : PcgAsyncPreviewNodeExecutor<UnionRegionsNode>
	{
		public PcgOutput<RegionSet> Result;

		public override bool IsEmpty => Result.Value == null || Result.Value.Count == 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			RegionSet input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Regions), ct);
			if (input == null)
			{
				Result.Value = new RegionSet();
				return;
			}

			var regions = PolygonClipper.Union(input.Regions, Array.Empty<Polygon2D>());
			PolygonClipper.RemoveSmallHoles(regions, Data.MinimumHoleArea);
			Result.Value = new RegionSet
			{
				PlaneY = input.PlaneY,
				Regions = regions
			};
		}

		public override void DrawPreview(Transform transform)
		{
			var options = GetGizmosOptions();
			Gizmos.matrix = transform.localToWorldMatrix;
			RegionGizmoUtility.Draw(Result.Value, options.Color, new Color(options.Color.r, options.Color.g, options.Color.b, options.Color.a * 0.5f));
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
