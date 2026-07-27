using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Splines.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Utilities;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.Splines
{
	public class SmoothSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<SmoothSplinesNode>
	{
		public PcgOutput<PcgSplineSet> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var iterationsInput = GetInputValue(nameof(Data.Iterations), Data.Iterations);
			var strengthInput = GetInputValue(nameof(Data.Strength), Data.Strength);

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
			{
				Results.Value = new PcgSplineSet();
				return;
			}

			var snapshots = new List<SmoothInput>();
			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in splinesList)
				{
					if (splines == null)
						continue;

					for (int s = 0; s < splines.Splines.Count; s++)
					{
						var spline = splines.Splines[s];
						if (spline == null)
							continue;

						var positions = new float3[spline.Count];
						for (int k = 0; k < spline.Count; k++)
						{
							positions[k] = spline[k].Position;
							await scope.Step(ct: ct);
						}
						snapshots.Add(new SmoothInput(spline, positions, spline.Closed, splines, s));
					}
				}
			}

			var smoothed = new float3[snapshots.Count][];
			var strength = math.clamp(strengthInput, 0f, 1f);
			var iterations = math.max(0, iterationsInput);
			await PcgWorkerScheduler.RunIndexedAsync(snapshots.Count, index =>
			{
				var snapshot = snapshots[index];
				if (snapshot.Positions.Length <= 2)
					return;

				var positions = (float3[])snapshot.Positions.Clone();
				for (int iter = 0; iter < iterations; iter++)
				{
					ct.ThrowIfCancellationRequested();
					var source = (float3[])positions.Clone();
					var first = snapshot.Closed ? 0 : 1;
					var last = snapshot.Closed ? positions.Length - 1 : positions.Length - 2;

					for (int k = first; k <= last; k++)
					{
						var prev = source[(k - 1 + source.Length) % source.Length];
						var next = source[(k + 1) % source.Length];
						positions[k] = math.lerp(source[k], (prev + next) * 0.5f, strength);
					}
				}
				smoothed[index] = positions;
			}, ct);

			var results = new PcgSplineSet(snapshots.Count);
			using (var scope = OperationScope.Start(this))
			{
				for (int i = 0; i < snapshots.Count; i++)
				{
					if (smoothed[i] == null)
					{
						results.AppendFrom(snapshots[i].SourceSet, snapshots[i].SourceIndex, snapshots[i].Original);
						continue;
					}

					var result = new Spline { Closed = snapshots[i].Closed };
					foreach (var position in smoothed[i])
					{
						result.Add(new BezierKnot(position, float3.zero, float3.zero), TangentMode.AutoSmooth);
						await scope.Step(ct: ct);
					}
					results.AppendFrom(snapshots[i].SourceSet, snapshots[i].SourceIndex, result);
				}
			}

			Results.Value = results;
		}

		public override void DrawPreview(Transform transform)
		{
			if (Results.Value == null)
				return;

			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value.Splines, transform);
		}

		private sealed class SmoothInput
		{
			public readonly Spline Original;
			public readonly float3[] Positions;
			public readonly bool Closed;
			public readonly PcgSplineSet SourceSet;
			public readonly int SourceIndex;

			public SmoothInput(Spline original, float3[] positions, bool closed, PcgSplineSet sourceSet, int sourceIndex)
			{
				Original = original;
				Positions = positions;
				Closed = closed;
				SourceSet = sourceSet;
				SourceIndex = sourceIndex;
			}
		}
	}
}
