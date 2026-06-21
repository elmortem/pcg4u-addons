using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Polygons
{
	public class SplineToRegionNodeExecutor : PcgAsyncPreviewNodeExecutor<SplineToRegionNode>
	{
		public PcgOutput<RegionSet> Result;

		public override bool IsEmpty => Result.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Result.Value = new RegionSet();

			var splines = GetInputValue(nameof(Data.Splines), Data.Splines);
			if (splines == null || splines.Count <= 0)
				return;

			var maxSegmentLength = GetInputValue(nameof(Data.MaxSegmentLength), Data.MaxSegmentLength);

			using (var scope = OperationScope.Start(this))
			{
				Result.Value = SplineRegionConvert.SplinesToRegions(splines, maxSegmentLength);
				await scope.Step(ct: ct);
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
