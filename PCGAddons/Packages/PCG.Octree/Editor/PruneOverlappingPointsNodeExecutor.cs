using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Octree
{
	public class PruneOverlappingPointsNodeExecutor : PcgAsyncPreviewNodeExecutor<PruneOverlappingPointsNode>, IPointsCount
	{
		public PcgOutput<PcgPointCloud> Out0;
		public PcgOutput<PcgPointCloud> Out1;
		public PcgOutput<PcgPointCloud> Out2;
		public PcgOutput<PcgPointCloud> Out3;

		public override bool IsEmpty => Out0.Value == null && Out1.Value == null && Out2.Value == null && Out3.Value == null;

		public int PointsCount => (Out0.Value?.Count ?? 0) + (Out1.Value?.Count ?? 0) + (Out2.Value?.Count ?? 0) + (Out3.Value?.Count ?? 0);

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var ports = new[]
			{
				GetInputValues(nameof(Data.In0), Data.In0),
				GetInputValues(nameof(Data.In1), Data.In1),
				GetInputValues(nameof(Data.In2), Data.In2),
				GetInputValues(nameof(Data.In3), Data.In3)
			};
			var radii = new[] { Data.Radius0, Data.Radius1, Data.Radius2, Data.Radius3 };
			var selfPrune = new[] { Data.SelfPrune0, Data.SelfPrune1, Data.SelfPrune2, Data.SelfPrune3 };
			var overlap = Data.Overlap;

			var work = PcgWorkerScheduler.RunAsync(() => OverlapPruneSolver.Prune(ports, radii, selfPrune, overlap, ct), ct);
			while (work.Status == UniTaskStatus.Pending)
			{
				PcgComputeSystem.ReportProgress(this);
				await UniTask.Delay(250, cancellationToken: ct);
			}

			var outputs = await work;
			Out0.Value = outputs[0];
			Out1.Value = outputs[1];
			Out2.Value = outputs[2];
			Out3.Value = outputs[3];
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();
			GizmosUtility.DrawPoints(this, Out0.Value, gizmosOptions, transform);
			GizmosUtility.DrawPoints(this, Out1.Value, gizmosOptions, transform);
			GizmosUtility.DrawPoints(this, Out2.Value, gizmosOptions, transform);
			GizmosUtility.DrawPoints(this, Out3.Value, gizmosOptions, transform);
		}
	}
}
