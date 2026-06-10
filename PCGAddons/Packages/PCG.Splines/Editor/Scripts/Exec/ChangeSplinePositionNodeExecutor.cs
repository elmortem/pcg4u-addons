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
using Random = UnityEngine.Random;

namespace PCG.Splines
{
	public class ChangeSplinePositionNodeExecutor : PcgAsyncPreviewNodeExecutor<ChangeSplinePositionNode>
	{
		public PcgOutput<List<Spline>> Results;

		private CancellationTokenSource _cancel;

		public override bool IsEmpty => Results.Value == null;

		public override void OnBind()
		{
			base.OnBind();

			if (Data.Seed <= 0)
				Data.Seed = UnityEngine.Random.Range(1, int.MaxValue);
		}

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<Spline>();

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var min = (float3)GetInputValue(nameof(Data.Min), Data.Min);
			var max = (float3)GetInputValue(nameof(Data.Max), Data.Max);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);

			_cancel = new CancellationTokenSource();
			var cancellationToken = _cancel.Token;

			var flatSplines = new List<Spline>();
			foreach (var splines in splinesList)
			{
				if (splines != null)
				{
					flatSplines.AddRange(splines);
				}
			}

			if (flatSplines.Count <= 0)
				return;

			var batchSize = PCG.MaxGeneratePoints;
			var batches = math.max(4, (int)math.ceil((float)flatSplines.Count / batchSize));
			batchSize = (int)math.ceil((float)flatSplines.Count / batches);
			var tasks = new List<UniTask>(batches);

			for (int i = 0; i < flatSplines.Count; i += batchSize)
			{
				if (cancellationToken.IsCancellationRequested)
					return;

				int end = math.min(i + batchSize, flatSplines.Count);
				var task = ProcessBatch(flatSplines, i, end, min, max, seed + i, cancellationToken);
				tasks.Add(task);
			}

			await UniTask.WhenAll(tasks);

			_cancel.Dispose();
			_cancel = null;
		}

		private async UniTask ProcessBatch(List<Spline> splines, int start, int end, float3 min, float3 max,
			int seed, CancellationToken cancellationToken)
		{
			await UniTask.SwitchToThreadPool();

			var batchResults = new List<Spline>();
			var localRandom = PcgRandom.Create(seed);

			for (int i = start; i < end; i++)
			{
				if (cancellationToken.IsCancellationRequested)
					return;

				var spline = splines[i];
				var modifiedSpline = new Spline
				{
					Closed = spline.Closed
				};

				for (var knotIndex = 0; knotIndex < spline.Count; knotIndex++)
				{
					var knot = spline[knotIndex];
					var randomOffset = localRandom.NextFloat3(min, max);

					var modifiedKnot = new BezierKnot(
						knot.Position + randomOffset,
						knot.TangentIn,
						knot.TangentOut,
						knot.Rotation
					);

					modifiedSpline.Add(modifiedKnot, spline.GetTangentMode(knotIndex));
				}

				batchResults.Add(modifiedSpline);
			}

			lock (Results.Value)
			{
				Results.Value.AddRange(batchResults);
			}
		}

		public override void CancelCompute()
		{
			if (_cancel != null)
			{
				_cancel.Cancel();
				_cancel = null;
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
		}
	}
}
