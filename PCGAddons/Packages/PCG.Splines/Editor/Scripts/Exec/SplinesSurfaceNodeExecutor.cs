using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Splines;
using PCG.Splines.Surfaces;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Points;
using PCG.Utilities;
using PCG.Exec;
using PCG.GraphModel;
using Random = UnityEngine.Random;

namespace PCG.CreatePoints
{
	public class SplinesSurfaceNodeExecutor : PcgAsyncPreviewNodeExecutor<SplinesSurfaceNode>, IPointsCount
	{
		public PcgOutput<PcgPointCloud> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		public override void OnBind()
		{
			base.OnBind();

			if (Data.Seed <= 0)
				Data.Seed = UnityEngine.Random.Range(1, int.MaxValue);
		}

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new PcgPointCloud();

			var splinesPort = GetInputPort(nameof(Data.Splines));
			var splinesList = splinesPort.GetInputValues();
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var count = GetInputValue(nameof(Data.Count), Data.Count);
			if (count <= 0)
				return;

			var offset = GetInputValue(nameof(Data.Offset), Data.Offset);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);

			var list = new List<PointData>();
			var times = new List<float>();
			var distances = new List<float>();
			var sourceSets = new List<PcgSplineSet>();
			var sourceRows = new List<int>();
			var sourceSplines = new List<Spline>();
			var sourceSplineIndices = new List<int>();
			int flatIndex = 0;
			using (var scope = OperationScope.Start(this))
			{
				foreach (PcgSplineSet splines in splinesList)
				{
					if (splines == null)
						continue;

					for (int s = 0; s < splines.Splines.Count; s++)
					{
						var spline = splines.Splines[s];
						if (spline == null)
						{
							flatIndex++;
							continue;
						}

						int start = list.Count;
						await SplinePoints.GetPoints(scope, list, times, distances, spline, Data.PointMode, count, offset, seed, ct);
						for (int k = start; k < list.Count; k++)
						{
							sourceSets.Add(splines);
							sourceRows.Add(s);
							sourceSplines.Add(spline);
							sourceSplineIndices.Add(flatIndex);
						}

						flatIndex++;
					}
				}
			}

			Results.Value = SplinePointAttributes.Build(list, times, distances, sourceSets, sourceRows, sourceSplines, sourceSplineIndices);
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			GizmosUtility.DrawPoints(this, Results.Value, gizmosOptions, transform);
		}
	}
}
