using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class AssignRoadClassByDepthNodeExecutor : PcgAsyncPreviewNodeExecutor<AssignRoadClassByDepthNode>
	{
		public PcgOutput<RegionSet> Result;

		public override bool IsEmpty => Result.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Blocks), ct);
			if (input == null)
			{
				Result.Value = new RegionSet();
				return;
			}

			var maxWidth = GetInputValue(nameof(Data.MaxWidth), Data.MaxWidth);
			var minDepth = GetInputValue(nameof(Data.MinDepth), Data.MinDepth);
			var maxDepth = GetInputValue(nameof(Data.MaxDepth), Data.MaxDepth);

			const int curveResolution = 256;
			var curveLut = new float[curveResolution + 1];
			for (int i = 0; i <= curveResolution; i++)
				curveLut[i] = Data.WidthByDepth.Evaluate(i / (float)curveResolution);

			Result.Value = await PcgWorkerScheduler.RunAsync(() =>
			{
				var result = input.Clone();
				foreach (var polygon in result.Regions)
				{
					ct.ThrowIfCancellationRequested();
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
						float scaled = math.saturate(k) * curveResolution;
						int curveIndex = math.min((int)scaled, curveResolution - 1);
						float curveValue = math.lerp(curveLut[curveIndex], curveLut[curveIndex + 1], scaled - curveIndex);
						float width = curveValue * maxWidth;
						polygon.SetEdge(CityAttributes.Width, e, width);
					}
				}

				return result;
			}, ct);
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
