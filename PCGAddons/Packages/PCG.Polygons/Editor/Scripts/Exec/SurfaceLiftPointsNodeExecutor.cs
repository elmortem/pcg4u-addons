using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Polygons.City
{
	public sealed class SurfaceLiftPointsNodeExecutor : PcgAsyncPreviewNodeExecutor<SurfaceLiftPointsNode>, IPointsCount
	{
		public PcgOutput<PcgPointCloud> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override UniTask DoComputeAsync(CancellationToken ct)
		{
			var inputs = GetInputValues(nameof(Data.Points), Data.Points);
			if (inputs == null || inputs.Length == 0)
			{
				Results.Rent(0);
				return UniTask.CompletedTask;
			}

			float height = GetInputValue(nameof(Data.Height), Data.Height);
			Results.Rent(inputs.TotalCount());
			foreach (PcgPointCloud cloud in inputs)
			{
				if (cloud == null)
					continue;
				for (int i = 0; i < cloud.Count; i++)
				{
					ct.ThrowIfCancellationRequested();
					PointData point = cloud[i];
					point.Position += new float3(0f, height, 0f);
					Results.Value.AppendFrom(cloud, i, point);
				}
			}

			return UniTask.CompletedTask;
		}

		public override void DrawPreview(Transform transform)
		{
			GizmosUtility.DrawPoints(this, Results.Value, GetGizmosOptions(), transform);
		}
	}
}
