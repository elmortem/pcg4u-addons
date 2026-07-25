using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Attributes;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class PolygonBooleanNodeExecutor : PcgAsyncPreviewNodeExecutor<PolygonBooleanNode>
	{
		public PcgOutput<RegionSet> Result;

		public override bool IsEmpty => Result.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Result.Value = new RegionSet();

			var a = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.A), ct);
			var b = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.B), ct);
			if (a == null || b == null)
				return;

			var mode = Data.Mode;
			var result = await PcgWorkerScheduler.RunAsync(() =>
			{
				Action<PcgAttributeSet, int> tag = (attrs, row) => attrs.Set(CityAttributes.Boundary, row, true);

				List<Polygon2D> polygons;
				switch (mode)
				{
					case PolygonBooleanMode.Union:
						polygons = PolygonEdgeClip.Union(a.Regions, b.Regions, tag);
						break;
					case PolygonBooleanMode.Intersection:
						polygons = PolygonEdgeClip.Intersection(a.Regions, b.Regions, tag);
						break;
					default:
						polygons = PolygonEdgeClip.Difference(a.Regions, b.Regions, tag);
						break;
				}

				var computed = new RegionSet
				{
					PlaneY = a.PlaneY
				};
				for (int i = 0; i < polygons.Count; i++)
				{
					ct.ThrowIfCancellationRequested();
					computed.AddRegion(polygons[i]);
				}

				return computed;
			}, ct);

			Result.Value = result;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();
			var outerColor = gizmosOptions.Color;
			var holeColor = new Color(outerColor.r, outerColor.g, outerColor.b, outerColor.a * 0.5f);

			Gizmos.matrix = transform.localToWorldMatrix;
			RegionGizmoUtility.Draw(Result.Value, outerColor, holeColor);
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
