using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Polygons.City
{
	public class LotFrontagePointsNodeExecutor : PcgAsyncPreviewNodeExecutor<LotFrontagePointsNode>, IPointsCount
	{
		public PcgOutput<PcgPointCloud> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var lots = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Lots), ct);
			var roads = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Roads), ct);
			if (lots == null || roads == null)
			{
				Results.Value = new PcgPointCloud();
				return;
			}

			var settings = new LotFrontageSettings
			{
				Setback = GetInputValue(nameof(Data.Setback), Data.Setback),
				MaxRoadDistance = GetInputValue(nameof(Data.MaxRoadDistance), Data.MaxRoadDistance),
				MinFrontage = GetInputValue(nameof(Data.MinFrontage), Data.MinFrontage),
				SetbackJitter = GetInputValue(nameof(Data.SetbackJitter), Data.SetbackJitter),
				Seed = GetInputValue(nameof(Data.Seed), Data.Seed),
				MinPlacementClearance = GetInputValue(nameof(Data.MinPlacementClearance), Data.MinPlacementClearance),
				MaxPlacementDistance = GetInputValue(nameof(Data.MaxPlacementDistance), Data.MaxPlacementDistance)
			};

			var work = PcgWorkerScheduler.RunAsync(() => LotFrontage.Build(lots, roads, settings, ct), ct);
			while (work.Status == UniTaskStatus.Pending)
			{
				PcgComputeSystem.ReportProgress(this);
				await UniTask.Delay(250, cancellationToken: ct);
			}

			Results.Value = await work;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();
			GizmosUtility.DrawPoints(this, Results.Value, gizmosOptions, transform);
		}
	}
}
