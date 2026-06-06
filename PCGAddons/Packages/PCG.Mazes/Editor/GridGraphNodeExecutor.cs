using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Mazes.Utilities;
using PCG.Points;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Mazes
{
	public class GridGraphNodeExecutor : PcgAsyncPreviewNodeExecutor<GridGraphNode>, IPointsCount
	{
		public PcgOutput<Graph> Result;
		public PcgOutput<List<PointData>> CenterPoints;

		public override bool IsEmpty => Result.Value == null || CenterPoints.Value == null;
		public int PointsCount => CenterPoints.Value?.Count ?? 0;
		public bool ShowCenterPoints { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Result.Value = new Graph();
			CenterPoints.Value = new();

			var size = GetInputValue(nameof(Data.Size), Data.Size);
			var cellSize = GetInputValue(nameof(Data.CellSize), Data.CellSize);

			using (var scope = OperationScope.Start(this))
			{
				await GraphBuilder.BuildGrid(scope, Result.Value, size.x, size.y, cellSize.x, cellSize.y, ct);

				var halfX = size.x * cellSize.x * 0.5f;
				var halfY = size.y * cellSize.y * 0.5f;

				for (int x = -1; x < size.x; x++)
				{
					for (int y = -1; y < size.y; y++)
					{
						float centerX = (x + 0.5f) * cellSize.x - halfX;
						float centerY = (y + 0.5f) * cellSize.y - halfY;
						CenterPoints.Value.Add(new PointData
							{ Position = new Vector3(centerX, 0f, centerY), Normal = Vector3.up, Scale = 1f });

						await scope.Step(ct: ct);
					}
				}
			}
		}

		public override void DrawPreview(Transform transform)
		{
			if (IsEmpty)
				return;

			var gizmosOptions = GetGizmosOptions();

			GraphGizmoUtility.DrawGraph(Result.Value, gizmosOptions, transform);

			if (ShowCenterPoints)
			{
				GizmosUtility.DrawPoints(CenterPoints.Value, gizmosOptions, transform);
			}
		}
	}
}
