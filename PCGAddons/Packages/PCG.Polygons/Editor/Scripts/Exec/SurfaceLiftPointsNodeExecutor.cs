using System.Collections.Generic;
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
		public PcgOutput<List<PointData>> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override UniTask DoComputeAsync(CancellationToken ct)
		{
			var inputs = GetInputValues(nameof(Data.Points), Data.Points);
			if (inputs == null || inputs.Length == 0)
			{
				Results.Value = new List<PointData>();
				return UniTask.CompletedTask;
			}
			var output = new List<PointData>(inputs.TotalCount());
			float height = GetInputValue(nameof(Data.Height), Data.Height);
			foreach (List<PointData> points in inputs)
			{
				if (points == null)
					continue;
				for (int i = 0; i < points.Count; i++)
				{
					ct.ThrowIfCancellationRequested();
					PointData point = points[i];
					point.Position += new float3(0f, height, 0f);
					output.Add(point);
				}
			}

			Results.Value = output;
			return UniTask.CompletedTask;
		}

		public override void DrawPreview(Transform transform)
		{
			GizmosUtility.DrawPoints(Results.Value, GetGizmosOptions(), transform);
		}
	}
}
