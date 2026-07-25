using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Splines.Utilities;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class ChangeSplinePositionNodeExecutor : PcgAsyncPreviewNodeExecutor<ChangeSplinePositionNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null;

		public override void OnBind()
		{
			base.OnBind();

			if (Data.Seed <= 0)
				Data.Seed = UnityEngine.Random.Range(1, int.MaxValue);
		}

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
			{
				Results.Value = new List<Spline>();
				return;
			}

			var min = (float3)GetInputValue(nameof(Data.Min), Data.Min);
			var max = (float3)GetInputValue(nameof(Data.Max), Data.Max);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);

			var flatSplines = new List<Spline>();
			foreach (var splines in splinesList)
			{
				if (splines != null)
				{
					flatSplines.AddRange(splines);
				}
			}

			if (flatSplines.Count <= 0)
			{
				Results.Value = new List<Spline>();
				return;
			}

			var snapshots = new SplineInputSnapshot[flatSplines.Count];
			using (var scope = OperationScope.Start(this))
			{
				for (int i = 0; i < flatSplines.Count; i++)
				{
					var spline = flatSplines[i];
					var knots = new BezierKnot[spline.Count];
					var modes = new TangentMode[spline.Count];
					for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
					{
						knots[knotIndex] = spline[knotIndex];
						modes[knotIndex] = spline.GetTangentMode(knotIndex);
						await scope.Step(ct: ct);
					}
					snapshots[i] = new SplineInputSnapshot(knots, modes, spline.Closed);
				}
			}

			var modifiedKnots = new BezierKnot[snapshots.Length][];
			await PcgWorkerScheduler.RunIndexedAsync(snapshots.Length, index =>
			{
				ct.ThrowIfCancellationRequested();
				var snapshot = snapshots[index];
				var localRandom = PcgRandom.Create(seed + index);
				var output = new BezierKnot[snapshot.Knots.Length];
				for (int knotIndex = 0; knotIndex < snapshot.Knots.Length; knotIndex++)
				{
					ct.ThrowIfCancellationRequested();
					var knot = snapshot.Knots[knotIndex];
					var randomOffset = localRandom.NextFloat3(min, max);
					output[knotIndex] = new BezierKnot(
						knot.Position + randomOffset,
						knot.TangentIn,
						knot.TangentOut,
						knot.Rotation);
				}
				modifiedKnots[index] = output;
			}, ct);

			var results = new List<Spline>(snapshots.Length);
			using (var scope = OperationScope.Start(this))
			{
				for (int i = 0; i < snapshots.Length; i++)
				{
					var snapshot = snapshots[i];
					var spline = new Spline { Closed = snapshot.Closed };
					for (int knotIndex = 0; knotIndex < modifiedKnots[i].Length; knotIndex++)
					{
						spline.Add(modifiedKnots[i][knotIndex], snapshot.Modes[knotIndex]);
						await scope.Step(ct: ct);
					}
					results.Add(spline);
				}
			}

			Results.Value = results;
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
		}

		private sealed class SplineInputSnapshot
		{
			public readonly BezierKnot[] Knots;
			public readonly TangentMode[] Modes;
			public readonly bool Closed;

			public SplineInputSnapshot(BezierKnot[] knots, TangentMode[] modes, bool closed)
			{
				Knots = knots;
				Modes = modes;
				Closed = closed;
			}
		}
	}
}
