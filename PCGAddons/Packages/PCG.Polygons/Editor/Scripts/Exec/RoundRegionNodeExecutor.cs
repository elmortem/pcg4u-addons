using System.Collections.Generic;
using System.Threading;
using Clipper2ZLib;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Polygons.City;
using PCG.Polygons.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons
{
	public sealed class RoundRegionNodeExecutor : PcgAsyncPreviewNodeExecutor<RoundRegionNode>
	{
		public PcgOutput<RegionSet> Result;

		public override bool IsEmpty => Result.Value == null || Result.Value.Count == 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (input == null)
			{
				Result.Value = new RegionSet();
				return;
			}

			float radius = math.max(0f, GetInputValue(nameof(Data.Radius), Data.Radius));
			if (radius <= 0.0001f)
			{
				Result.Value = input.Clone();
				return;
			}

			var result = await PcgWorkerScheduler.RunAsync(() =>
			{
				var computed = new RegionSet
				{
					PlaneY = input.PlaneY
				};

				for (int i = 0; i < input.Regions.Count; i++)
				{
					var current = new List<Polygon2D> { input.Regions[i] };
					current = PolygonClipper.Inflate(current, -radius, JoinType.Round);
					current = PolygonClipper.Inflate(current, radius, JoinType.Round);
					current = PolygonClipper.Inflate(current, radius, JoinType.Round);
					current = PolygonClipper.Inflate(current, -radius, JoinType.Round);

					for (int p = 0; p < current.Count; p++)
					{
						computed.Regions.Add(current[p]);
						if (i < input.Attributes.Count)
							computed.Attributes.AppendRow(input.Attributes, i);
						else
							computed.Attributes.AddRow();
					}
					ct.ThrowIfCancellationRequested();
				}

				return computed;
			}, ct);

			Result.Value = result;
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
