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
		public PcgOutput<PcgPointCloud> Results;
		public PcgOutput<PcgPointCloud> CornerPoints;

		public override bool IsEmpty => Results.Value == null || CornerPoints.Value == null;
		public int PointsCount => ShowResults ? (Results.Value?.Count ?? 0) : (CornerPoints.Value?.Count ?? 0);
		public bool ShowResults { get; set; } = true;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new PcgPointCloud();
			CornerPoints.Value = new PcgPointCloud();

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

			var results = new OffsetPointBuffer();
			var corners = new OffsetPointBuffer();
			int flatIndex = 0;
			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in splinesList)
				{
					if (splines == null || splines.Count <= 0)
						continue;

					for (int s = 0; s < splines.Splines.Count; s++)
					{
						var spline = splines.Splines[s];
						if (spline.Count <= 1)
						{
							flatIndex++;
							continue;
						}

						int resultsStart = results.Points.Count;
						int cornersStart = corners.Points.Count;
						var length = spline.GetLength();

						if (Data.Spacing == SplineSpacingMode.Distance)
						{
							if (Data.Placement == SplinePointPlacement.SegmentCenters)
							{
								var total = math.max(1, (int)math.floor(length / distance));
								var margin = (length - total * distance) * 0.5f;
								for (int i = 0; i < total; i++)
								{
									EvaluateAndAdd(spline, margin + (i + 0.5f) * distance, offset, results);
									await scope.Step(ct: ct);
								}
							}
							else
							{
								for (float dist = 0f; dist <= length; dist += distance)
								{
									EvaluateAndAdd(spline, dist, offset, results);
									await scope.Step(ct: ct);
								}
							}
						}
						else if (Data.Spacing == SplineSpacingMode.Count)
						{
							var total = math.max(1, count);
							if (Data.Placement == SplinePointPlacement.SegmentCenters)
							{
								var step = length / total;
								for (int i = 0; i < total; i++)
								{
									EvaluateAndAdd(spline, (i + 0.5f) * step, offset, results);
									await scope.Step(ct: ct);
								}
							}
							else
							{
								var lastIndex = spline.Closed ? total : total - 1;
								var step = total == 1 ? 0f : length / lastIndex;
								for (int i = 0; i < total; i++)
								{
									EvaluateAndAdd(spline, i * step, offset, results);
									await scope.Step(ct: ct);
								}
							}
						}
						else
						{
							var steps = math.max(1, (int)math.round(length / distance));
							var step = length / steps;
							if (Data.Placement == SplinePointPlacement.SegmentCenters)
							{
								for (int i = 0; i < steps; i++)
								{
									EvaluateAndAdd(spline, (i + 0.5f) * step, offset, results);
									await scope.Step(ct: ct);
								}
							}
							else
							{
								var lastIndex = spline.Closed ? steps - 1 : steps;
								for (int i = 0; i <= lastIndex; i++)
								{
									EvaluateAndAdd(spline, i * step, offset, results);
									await scope.Step(ct: ct);
								}
							}
						}

						for (int k = 0; k < spline.Count; k++)
						{
							var knotT = SplineUtility.ConvertIndexUnit(spline, k, PathIndexUnit.Knot, PathIndexUnit.Normalized);
							var knotDistance = SplineUtility.ConvertIndexUnit(spline, knotT, PathIndexUnit.Normalized, PathIndexUnit.Distance);
							EvaluateAndAddAtT(spline, knotT, knotDistance, offset, corners);
							await scope.Step(ct: ct);
						}

						results.FillSource(resultsStart, splines, s, flatIndex);
						corners.FillSource(cornersStart, splines, s, flatIndex);
						flatIndex++;
					}
				}
			}

			Results.Value = results.BuildCloud(true);
			CornerPoints.Value = corners.BuildCloud(false);
		}

		private void EvaluateAndAdd(Spline spline, float dist, float offset, OffsetPointBuffer target)
		{
			var t = SplineUtility.ConvertIndexUnit(spline, dist, PathIndexUnit.Distance, PathIndexUnit.Normalized);
			EvaluateAndAddAtT(spline, math.clamp(t, 0f, 1f), dist, offset, target);
		}

		private void EvaluateAndAddAtT(Spline spline, float t, float dist, float offset, OffsetPointBuffer target)
		{
			spline.Evaluate(t, out var point, out var tangent, out var upVector);
			var width = SplineWidthUtility.Evaluate(spline, t, 0f);
			var effectiveOffset = offset;

			if (Data.UseSplineWidth)
			{
				effectiveOffset += width * Data.WidthMultiplier;
			}

			if (math.abs(effectiveOffset) < 0.0001f)
			{
				AddPoint(target, point, tangent, upVector, t, dist, width, 0);
				return;
			}

			var cross = math.cross(tangent, upVector);
			var crossLengthSq = math.lengthsq(cross);
			if (crossLengthSq < 1e-10f || !math.isfinite(crossLengthSq))
				return;

			var offsetDirection = cross * math.rsqrt(crossLengthSq);

			if (Data.BothSides)
			{
				AddPoint(target, point + offsetDirection * math.abs(effectiveOffset), tangent, upVector, t, dist, width, 1);
				AddPoint(target, point - offsetDirection * math.abs(effectiveOffset), tangent, upVector, t, dist, width, -1);
			}
			else
			{
				AddPoint(target, point + offsetDirection * effectiveOffset, tangent, upVector, t, dist, width, 0);
			}
		}

		private void AddPoint(OffsetPointBuffer target, float3 pos, float3 tangent, float3 upVector, float t, float dist, float width, int side)
		{
			target.Points.Add(new PointData
			{
				Position = pos,
				Normal = Data.UpNormal ? Vector3.up : upVector,
				Scale = 1f,
				Angle = Data.NoRotation ? 0f : Quaternion.LookRotation(tangent, upVector).eulerAngles.y
			});
			target.Times.Add(t);
			target.Distances.Add(dist);
			target.Widths.Add(width);
			target.Sides.Add(side);
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			if (ShowResults)
				GizmosUtility.DrawPoints(this, Results.Value, gizmosOptions, transform);
			else
				GizmosUtility.DrawPoints(this, CornerPoints.Value, gizmosOptions, transform);
		}
	}
}
