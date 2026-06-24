using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Splines.Utilities;
using PCG.Utilities;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Polygons
{
	public class RegionToSplineNodeExecutor : PcgAsyncPreviewNodeExecutor<RegionToSplineNode>
	{
		public PcgOutput<List<Spline>> Splines;

		public override bool IsEmpty => Splines.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Splines.Value = new List<Spline>();

			var region = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (region == null)
				return;

			using (var scope = OperationScope.Start(this))
			{
				Splines.Value = SplineRegionConvert.RegionsToSplines(region);
				await scope.Step(ct: ct);
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Splines.Value, transform);
		}
	}
}
