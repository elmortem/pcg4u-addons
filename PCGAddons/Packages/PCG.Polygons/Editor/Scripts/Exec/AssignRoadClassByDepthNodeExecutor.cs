using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class AssignRoadClassByDepthNodeExecutor : PcgAsyncPreviewNodeExecutor<AssignRoadClassByDepthNode>
	{
		public PcgOutput<RegionSet> Result;

		public override bool IsEmpty => Result.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Result.Value = new RegionSet();

			var input = GetInputValue(nameof(Data.Blocks), Data.Blocks);
			if (input == null)
				return;

			var maxWidth = GetInputValue(nameof(Data.MaxWidth), Data.MaxWidth);
			var minDepth = GetInputValue(nameof(Data.MinDepth), Data.MinDepth);
			var maxDepth = GetInputValue(nameof(Data.MaxDepth), Data.MaxDepth);

			var result = input.Clone();

			using (var scope = OperationScope.Start(this))
			{
				foreach (var polygon in result.Regions)
				{
					if (!polygon.HasEdgeData())
						continue;

					if (!polygon.EdgeAttributes.HasColumn(CityAttributes.CutDepth))
						continue;

					for (int e = 0; e < polygon.EdgeCount; e++)
					{
						int d = polygon.GetEdge<int>(CityAttributes.CutDepth, e);
						if (d < minDepth || d > maxDepth)
							continue;

						float k = maxDepth > 0 ? (float)d / maxDepth : 0f;
						float width = Data.WidthByDepth.Evaluate(k) * maxWidth;
						polygon.SetEdge(CityAttributes.Width, e, width);
					}

					await scope.Step(ct: ct);
				}

				Result.Value = result;
			}
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
