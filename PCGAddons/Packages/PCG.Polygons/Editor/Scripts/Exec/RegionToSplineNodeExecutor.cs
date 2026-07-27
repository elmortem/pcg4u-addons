using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.City;
using PCG.Splines;
using PCG.Splines.Utilities;
using PCG.Utilities;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Polygons
{
	public class RegionToSplineNodeExecutor : PcgAsyncPreviewNodeExecutor<RegionToSplineNode>
	{
		public PcgOutput<PcgSplineSet> Splines;

		public override bool IsEmpty => Splines.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Splines.Value = new PcgSplineSet();

			var region = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);
			if (region == null)
				return;

			using (var scope = OperationScope.Start(this))
			{
				var splines = SplineRegionConvert.RegionsToSplines(region);
				var sourceRegionRow = new List<int>(splines.Count);
				for (int i = 0; i < region.Regions.Count; i++)
				{
					sourceRegionRow.Add(i);
					for (int h = 0; h < region.Regions[i].Holes.Count; h++)
					{
						sourceRegionRow.Add(i);
					}
				}

				var set = new PcgSplineSet(splines.Count);
				for (int k = 0; k < splines.Count; k++)
				{
					set.Splines.Add(splines[k]);
					set.Attributes.AppendRow(region.Attributes, sourceRegionRow[k]);
				}

				var regionIndexColumn = set.Attributes.EnsureColumn<int>(CityAttributes.RegionIndex);
				for (int k = 0; k < splines.Count; k++)
				{
					regionIndexColumn.Values[k] = sourceRegionRow[k];
				}

				Splines.Value = set;
				await scope.Step(ct: ct);
			}
		}

		public override void DrawPreview(Transform transform)
		{
			if (Splines.Value == null)
				return;

			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Splines.Value.Splines, transform);
		}
	}
}
