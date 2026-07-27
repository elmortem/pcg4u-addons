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
		public PcgOutput<PcgSplineSet> Results;

		public override bool IsEmpty => Results.Value == null || Results.Value.Count == 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			var flat = SplineNetworkInput.Flatten(splinesList);
			if (flat.Count == 0)
			{
				Results.Value = new PcgSplineSet();
				return;
			}

			var flatSets = new List<PcgSplineSet>(flat.Count);
			var flatIndices = new List<int>(flat.Count);
			foreach (var splines in splinesList)
			{
				if (splines == null)
					continue;

				for (int i = 0; i < splines.Splines.Count; i++)
				{
					flatSets.Add(splines);
					flatIndices.Add(i);
				}
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
				var passList = new PcgSplineSet(flat.Count);
				var passSourceRow = new List<int>(flat.Count);
				for (int i = 0; i < flat.Count; i++)
				{
					if (flat[i] != null)
					{
						passList.AppendFrom(flatSets[i], flatIndices[i]);
						passSourceRow.Add(i);
					}
				}

				WritePieceAttributes(passList, passSourceRow, null, null);
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

			var results = new PcgSplineSet();
			var sourceRow = new List<int>();
			var pieceRow = new List<int>();
			var incidenceRow = new List<SplinePieceIncidence>();
			using var buildScope = OperationScope.Start(this);
			for (int i = 0; i < flat.Count; i++)
			{
				var spline = flat[i];
				if (spline == null)
					continue;

				var pieces = solved.Pieces[i];
				if (pieces == null)
				{
					results.AppendFrom(flatSets[i], flatIndices[i]);
					sourceRow.Add(i);
					pieceRow.Add(0);
					incidenceRow.Add(new SplinePieceIncidence { StartJunction = -1, EndJunction = -1 });
					continue;
				}

				var incidence = solved.PieceIncidence[i];
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

					results.AppendFrom(flatSets[i], flatIndices[i], built);
					sourceRow.Add(i);
					pieceRow.Add(p);
					incidenceRow.Add(incidence != null && p < incidence.Count
						? incidence[p]
						: new SplinePieceIncidence { StartJunction = -1, EndJunction = -1 });
				}
			}

			WritePieceAttributes(results, sourceRow, pieceRow, incidenceRow);
			Results.Value = results;
		}

		private static void WritePieceAttributes(PcgSplineSet set, List<int> sourceRow, List<int> pieceRow, List<SplinePieceIncidence> incidenceRow)
		{
			var sourceColumn = set.Attributes.EnsureColumn<int>(SplineAttributes.SourceSplineIndex);
			var pieceColumn = set.Attributes.EnsureColumn<int>(SplineAttributes.PieceIndex);
			var startColumn = set.Attributes.EnsureColumn<int>(SplineAttributes.StartJunction);
			var endColumn = set.Attributes.EnsureColumn<int>(SplineAttributes.EndJunction);
			for (int i = 0; i < set.Count; i++)
			{
				sourceColumn.Values[i] = sourceRow[i];
				pieceColumn.Values[i] = pieceRow != null ? pieceRow[i] : 0;
				startColumn.Values[i] = incidenceRow != null ? incidenceRow[i].StartJunction : -1;
				endColumn.Values[i] = incidenceRow != null ? incidenceRow[i].EndJunction : -1;
			}
		}

		private static List<float3> FlattenPoints(PcgPointCloud[] pointsList)
		{
			var result = new List<float3>();
			if (pointsList == null)
				return result;

			foreach (var cloud in pointsList)
			{
				if (cloud == null)
					continue;

				foreach (var point in cloud.Points)
					result.Add(point.Position);
			}

			return result;
		}

		public override void DrawPreview(Transform transform)
		{
			if (Results.Value == null)
				return;

			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value.Splines, transform);
		}
	}
}
