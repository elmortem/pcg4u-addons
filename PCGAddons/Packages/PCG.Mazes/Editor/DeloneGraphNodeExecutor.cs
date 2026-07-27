using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Delone;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Mazes.Utilities;
using PCG.Points;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Mazes
{
	public class DeloneGraphNodeExecutor : PcgAsyncPreviewNodeExecutor<DeloneGraphNode>, IPointsCount, IShowCenterPoints
	{
		public PcgOutput<Graph> Result;
		public PcgOutput<PcgPointCloud> CenterPoints;

		public override bool IsEmpty => Result.Value == null || CenterPoints.Value == null;
		public int PointsCount => CenterPoints.Value?.Count ?? 0;
		public bool ShowCenterPoints { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Result.Value = new Graph();
			CenterPoints.Value = new();

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var minDistance = GetInputValue(nameof(Data.MinDistance), Data.MinDistance);
			var minRatio = GetInputValue(nameof(Data.MinRatio), Data.MinRatio);

			using (var scope = OperationScope.Start(this))
			{
				var triPoints = new List<Vector2>();
				float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
				foreach (PcgPointCloud cloud in pointsList)
				{
					foreach (var point in cloud)
					{
						var p = new Vector2(point.Position.x, point.Position.z);
						triPoints.Add(p);

						if (p.x < minX)
							minX = p.x;
						if (p.x > maxX)
							maxX = p.x;
						if (p.y < minY)
							minY = p.y;
						if (p.y > maxY)
							maxY = p.y;

						await scope.Step(ct: ct);
					}
				}

				var minPoint = new Vector2(minX - 10f, minY - 10f);
				var maxPoint = new Vector2(maxX + 10f, maxY + 10f);

				var triangulation = new Triangulation(triPoints, minPoint, maxPoint);
				triangulation.Calc();

				await GenerateGraph(scope, triangulation.Triangles, minX, minY, maxX, maxY, minDistance, minRatio, ct);

				foreach (var triangle in triangulation.Triangles)
				{
					var p = triangle.Centroid;
					CenterPoints.Value.Add(new PointData { Position = new Vector3(p.x, 0f, p.y), Normal = Vector3.up, Scale = 1f });

					await scope.Step(ct: ct);
				}
			}
		}

		private async UniTask GenerateGraph(OperationScope scope, List<Triangle> triangles, float minX, float minY, float maxX, float maxY, float minDistance, float minRatio, CancellationToken ct)
		{
			var trianglesList = new List<Triangle>();
			var distances = new List<float>();
			foreach (var triangle in triangles)
			{
				var ok = true;
				foreach (var point in triangle.Points)
				{
					if (point.x <= minX || point.x >= maxX || point.y <= minY || point.y >= maxY)
					{
						ok = false;
						break;
					}
					await scope.Step(ct: ct);
				}

				if (ok)
				{
					if ((triangle.Points[0] - triangle.Points[1]).Magnitude() > minDistance ||
						(triangle.Points[1] - triangle.Points[2]).Magnitude() > minDistance ||
						(triangle.Points[2] - triangle.Points[0]).Magnitude() > minDistance)
					{
						ok = false;
					}
				}

				if (ok)
				{
					distances.Clear();
					distances.Add((triangle.Points[0] - triangle.Points[1]).Magnitude());
					distances.Add((triangle.Points[1] - triangle.Points[2]).Magnitude());
					distances.Add((triangle.Points[2] - triangle.Points[0]).Magnitude());

					distances.Sort();
					if (distances[0] / distances[2] < minRatio)
					{
						ok = false;
					}
				}

				if (!ok)
					continue;

				trianglesList.Add(triangle);

				await scope.Step(ct: ct);
			}

			await GraphBuilder.BuildGraph(scope, Result.Value, trianglesList, ct);
		}

		public override void DrawPreview(Transform transform)
		{
			if (IsEmpty)
				return;

			var gizmosOptions = GetGizmosOptions();

			GraphGizmoUtility.DrawGraph(Result.Value, gizmosOptions, transform);

			if (ShowCenterPoints)
			{
				GizmosUtility.DrawPoints(this, CenterPoints.Value, gizmosOptions, transform);
			}
		}
	}
}
