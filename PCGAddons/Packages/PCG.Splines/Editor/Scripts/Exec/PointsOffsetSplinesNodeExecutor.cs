using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;
using PCG.Splines;
using PCG.Utilities;
using PCG.Exec;

namespace PCG.CreatePoints
{
	public class PointsOffsetSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<PointsOffsetSplinesNode>, IPointsCount, IShowResults
	{
		public PcgOutput<List<PointData>> Results;
		public PcgOutput<List<PointData>> CornerPoints;

		public override bool IsEmpty => Results.Value == null || CornerPoints.Value == null;
		public int PointsCount => ShowResults ? (Results.Value?.Count ?? 0) : (CornerPoints.Value?.Count ?? 0);
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<PointData>();
			CornerPoints.Value = new List<PointData>();

			var offset = GetInputValue(nameof(Data.Offset), Data.Offset);
			var distance = GetInputValue(nameof(Data.Distance), Data.Distance);
			var count = GetInputValue(nameof(Data.Count), Data.Count);

			if (Data.Spacing != SplineSpacingMode.Count && distance < 0.0001f)
				return;
			if (Data.Spacing == SplineSpacingMode.Count && count <= 0)
				return;

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return;

			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in splinesList)
				{
					if (splines == null || splines.Count <= 0)
						continue;

					foreach (var spline in splines)
					{
						if (spline.Count <= 1)
							continue;

						var length = spline.GetLength();

						if (Data.Spacing == SplineSpacingMode.Distance)
						{
							for (float dist = 0f; dist <= length; dist += distance)
							{
								EvaluateAndAdd(spline, dist, offset, Results.Value);
								await scope.Step(ct: ct);
							}
						}
						else if (Data.Spacing == SplineSpacingMode.Count)
						{
							var total = math.max(1, count);
							var lastIndex = spline.Closed ? total : total - 1;
							var step = total == 1 ? 0f : length / lastIndex;
							for (int i = 0; i < total; i++)
							{
								EvaluateAndAdd(spline, i * step, offset, Results.Value);
								await scope.Step(ct: ct);
							}
						}
						else
						{
							var steps = math.max(1, (int)math.round(length / distance));
							var step = length / steps;
							var lastIndex = spline.Closed ? steps - 1 : steps;
							for (int i = 0; i <= lastIndex; i++)
							{
								EvaluateAndAdd(spline, i * step, offset, Results.Value);
								await scope.Step(ct: ct);
							}
						}

						for (int k = 0; k < spline.Count; k++)
						{
							var knotT = SplineUtility.ConvertIndexUnit(spline, k, PathIndexUnit.Knot, PathIndexUnit.Normalized);
							EvaluateAndAddAtT(spline, knotT, offset, CornerPoints.Value);
							await scope.Step(ct: ct);
						}
					}
				}
			}
		}

		private void EvaluateAndAdd(Spline spline, float dist, float offset, List<PointData> target)
		{
			var t = SplineUtility.ConvertIndexUnit(spline, dist, PathIndexUnit.Distance, PathIndexUnit.Normalized);
			EvaluateAndAddAtT(spline, math.clamp(t, 0f, 1f), offset, target);
		}

		private void EvaluateAndAddAtT(Spline spline, float t, float offset, List<PointData> target)
		{
			spline.Evaluate(t, out var point, out var tangent, out var upVector);

			if (math.abs(offset) < 0.0001f)
			{
				AddPoint(target, point, tangent, upVector);
				return;
			}

			var offsetDirection = math.normalize(math.cross(tangent, upVector));

			if (Data.BothSides)
			{
				AddPoint(target, point + offsetDirection * math.abs(offset), tangent, upVector);
				AddPoint(target, point - offsetDirection * math.abs(offset), tangent, upVector);
			}
			else
			{
				AddPoint(target, point + offsetDirection * offset, tangent, upVector);
			}
		}

		private void AddPoint(List<PointData> target, float3 pos, float3 tangent, float3 upVector)
		{
			target.Add(new PointData
			{
				Position = pos,
				Normal = Data.UpNormal ? Vector3.up : upVector,
				Scale = 1f,
				Angle = Data.NoRotation ? 0f : Quaternion.LookRotation(tangent, upVector).eulerAngles.y
			});
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			if (ShowResults)
				GizmosUtility.DrawPoints(Results.Value, gizmosOptions, transform);
			else
				GizmosUtility.DrawPoints(CornerPoints.Value, gizmosOptions, transform);
		}
	}
}
