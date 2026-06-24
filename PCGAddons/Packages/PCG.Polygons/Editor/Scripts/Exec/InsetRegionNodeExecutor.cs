using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class InsetRegionNodeExecutor : PcgAsyncPreviewNodeExecutor<InsetRegionNode>
	{
		public PcgOutput<RegionSet> Result;

		public override bool IsEmpty => Result.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Result.Value = new RegionSet();

			var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (input == null)
				return;

			var delta = GetInputValue(nameof(Data.Delta), Data.Delta);

			var result = new RegionSet();
			result.PlaneY = input.PlaneY;
			var single = new List<Polygon2D>(1);

			using (var scope = OperationScope.Start(this))
			{
				for (int i = 0; i < input.Regions.Count; i++)
				{
					single.Clear();
					single.Add(input.Regions[i]);
					var inflated = PolygonClipper.Inflate(single, delta);
					for (int j = 0; j < inflated.Count; j++)
					{
						result.Regions.Add(inflated[j]);
						result.Attributes.AppendRow(input.Attributes, i);
					}

					await scope.Step(ct: ct);
				}
			}

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
