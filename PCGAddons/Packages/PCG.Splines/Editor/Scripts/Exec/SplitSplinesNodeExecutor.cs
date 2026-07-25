using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Splines.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class SplitSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<SplitSplinesNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null || Results.Value.Count == 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			var flat = SplineNetworkInput.Flatten(splinesList);
			if (flat.Count == 0)
			{
				Results.Value = new List<Spline>();
				return;
			}

			var cutsTopology = GetInputValue(nameof(Data.Cuts), Data.Cuts);
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			var points = FlattenPoints(pointsList);
			var snapDistance = GetInputValue(nameof(Data.SnapDistance), Data.SnapDistance);

			var topologyCuts = cutsTopology?.Cuts;
			var hasCuts = topologyCuts != null && topologyCuts.Count > 0;
			var hasPoints = points.Count > 0 && snapDistance > 0f;

			if (!hasCuts && !hasPoints)
			{
				var passList = new List<Spline>(flat.Count);
				for (int i = 0; i < flat.Count; i++)
				{
					if (flat[i] != null)
						passList.Add(flat[i]);
				}
				Results.Value = passList;
				return;
			}

			var snapshots = new SplineSnapshot[flat.Count];
			using (var scope = OperationScope.Start(this))
			{
				for (int i = 0; i < flat.Count; i++)
				{
					var spline = flat[i];
					if (spline != null && spline.Count >= 2)
						snapshots[i] = SplineSnapshot.Capture(spline);

					await scope.Step(ct: ct);
				}
			}

			var solved = await PcgWorkerScheduler.RunAsync(
				() => SplineSplitSolver.Solve(snapshots, topologyCuts, points, snapDistance, ct,
					() => PcgComputeSystem.ReportProgress(this)),
				ct);

			if (solved.EmbeddedDataWarning)
				Debug.LogWarning("[Split Splines] Embedded spline data and knot links are not transferred to the resulting pieces.");
			if (solved.InvalidValues)
				Debug.LogWarning("[Split Splines] NaN or infinite values in cuts or points were discarded.");

			var results = new List<Spline>();
			using var buildScope = OperationScope.Start(this);
			for (int i = 0; i < flat.Count; i++)
			{
				var spline = flat[i];
				if (spline == null)
					continue;

				var pieces = solved.Pieces[i];
				if (pieces == null)
				{
					results.Add(spline);
					continue;
				}

				for (int p = 0; p < pieces.Count; p++)
				{
					var piece = pieces[p];
					if (piece.Count < 2)
						continue;

					var built = new Spline { Closed = false };
					for (int k = 0; k < piece.Count; k++)
					{
						var instruction = piece[k];
						built.Add(instruction.Knot, instruction.Mode, instruction.Tension);
						await buildScope.Step(ct: ct);
					}
					results.Add(built);
				}
			}

			Results.Value = results;
		}

		private static List<float3> FlattenPoints(List<PointData>[] pointsList)
		{
			var result = new List<float3>();
			if (pointsList == null)
				return result;

			foreach (var list in pointsList)
			{
				if (list == null)
					continue;

				foreach (var point in list)
					result.Add(point.Position);
			}

			return result;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
		}
	}
}
