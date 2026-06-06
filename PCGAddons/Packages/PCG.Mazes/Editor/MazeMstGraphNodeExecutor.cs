using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Mazes.Graphs;
using PCG.Mazes.Utilities;
using PCG.Points;
using PCG.Utilities;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PCG.Mazes
{
	public class MazeMstGraphNodeExecutor : PcgAsyncPreviewNodeExecutor<MazeMstGraphNode>
	{
		public PcgOutput<Graph> Result;
		public PcgOutput<List<PointData>> EndPoints;

		public override bool IsEmpty => Result.Value == null || EndPoints.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Result.Value = new();
			EndPoints.Value = new();

			var inputGraph = GetInputValue(nameof(Data.Graph), Data.Graph);
			if (inputGraph == null || inputGraph.Edges.Count <= 0)
				return;

			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);
			if (seed == -1)
				seed = Random.Range(1, int.MaxValue);

			RandomUtility.PushSeed(seed);

			using (var scope = OperationScope.Start(this))
			{
				foreach (var edge in inputGraph.Edges)
				{
					edge.Weight = RandomUtility.Range01();
				}

				await MazeGenerator.GenerateMaze(scope, inputGraph, Result.Value, ct);

				EndPoints.Value.AddRange(Result.Value.Nodes.Where(node => node.Edges.Count == 1).Select(p =>
					new PointData
					{
						Position = new Vector3(p.Point.x, 0f, p.Point.y), Normal = Vector3.up, Scale = 1f
					}));
			}

			RandomUtility.PopSeed();
		}

		public override void DrawPreview(Transform transform)
		{
			if (IsEmpty)
				return;

			var gizmosOptions = GetGizmosOptions();

			GraphGizmoUtility.DrawGraph(Result.Value, gizmosOptions, transform);
			GizmosUtility.DrawPoints(EndPoints.Value, gizmosOptions, transform);
		}
	}
}
