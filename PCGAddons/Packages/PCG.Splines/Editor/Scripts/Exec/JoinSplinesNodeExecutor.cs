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
	public class JoinSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<JoinSplinesNode>
	{
		public PcgOutput<PcgSplineSet> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new PcgSplineSet();

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var threshold = math.max(0f, GetInputValue(nameof(Data.Threshold), Data.Threshold));
			var thresholdSq = threshold * threshold;
			var chains = new List<List<float3>>();
			var chainSets = new List<PcgSplineSet>();
			var chainIndices = new List<int>();

			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in splinesList)
				{
					if (splines == null)
						continue;

					for (int s = 0; s < splines.Splines.Count; s++)
					{
						var spline = splines.Splines[s];
						if (spline.Count <= 1)
							continue;

						if (spline.Closed)
						{
							Results.Value.AppendFrom(splines, s);
							continue;
						}

						var chain = new List<float3>(spline.Count);
						for (int k = 0; k < spline.Count; k++)
							chain.Add(spline[k].Position);
						chains.Add(chain);
						chainSets.Add(splines);
						chainIndices.Add(s);

						await scope.Step(ct: ct);
					}
				}

				while (chains.Count > 0)
				{
					var current = chains[0];
					var currentSet = chainSets[0];
					var currentIndex = chainIndices[0];
					chains.RemoveAt(0);
					chainSets.RemoveAt(0);
					chainIndices.RemoveAt(0);

					var merged = true;
					while (merged)
					{
						merged = false;
						for (int i = 0; i < chains.Count; i++)
						{
							var candidate = chains[i];

							if (math.distancesq(current[^1], candidate[0]) <= thresholdSq)
							{
								Append(current, candidate, false);
							}
							else if (math.distancesq(current[^1], candidate[^1]) <= thresholdSq)
							{
								Append(current, candidate, true);
							}
							else if (math.distancesq(current[0], candidate[^1]) <= thresholdSq)
							{
								current.Reverse();
								Append(current, candidate, true);
								current.Reverse();
							}
							else if (math.distancesq(current[0], candidate[0]) <= thresholdSq)
							{
								current.Reverse();
								Append(current, candidate, false);
								current.Reverse();
							}
							else
							{
								continue;
							}

							chains.RemoveAt(i);
							chainSets.RemoveAt(i);
							chainIndices.RemoveAt(i);
							merged = true;
							break;
						}

						await scope.Step(ct: ct);
					}

					var closed = current.Count >= 3 && math.distancesq(current[0], current[^1]) <= thresholdSq;
					if (closed && math.distancesq(current[0], current[^1]) < 1e-8f)
						current.RemoveAt(current.Count - 1);

					var result = new Spline
					{
						Closed = closed
					};
					foreach (var position in current)
						result.Add(new BezierKnot(position, float3.zero, float3.zero), TangentMode.AutoSmooth);

					Results.Value.AppendFrom(currentSet, currentIndex, result);
				}
			}
		}

		private static void Append(List<float3> target, List<float3> source, bool reversed)
		{
			var count = source.Count;
			for (int i = 0; i < count; i++)
			{
				var position = reversed ? source[count - 1 - i] : source[i];
				if (i == 0 && math.distancesq(target[^1], position) < 1e-8f)
					continue;
				target.Add(position);
			}
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
