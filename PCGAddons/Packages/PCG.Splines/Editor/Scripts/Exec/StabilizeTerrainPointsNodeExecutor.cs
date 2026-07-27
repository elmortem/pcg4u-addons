using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.TransformPoints
{
	public sealed class StabilizeTerrainPointsNodeExecutor : PcgAsyncPreviewNodeExecutor<StabilizeTerrainPointsNode>, IPointsCount
	{
		public PcgOutput<PcgPointCloud> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length == 0)
			{
				Results.Rent(0);
				return;
			}

			var totalCount = pointsList.TotalCount();
			var flatPoints = new List<PointData>(totalCount);
			var flatClouds = new List<PcgPointCloud>(totalCount);
			var flatIndices = new List<int>(totalCount);
			foreach (PcgPointCloud cloud in pointsList)
			{
				if (cloud == null || cloud.Count == 0)
					continue;

				for (int idx = 0; idx < cloud.Count; idx++)
				{
					flatPoints.Add(cloud[idx]);
					flatClouds.Add(cloud);
					flatIndices.Add(idx);
				}
			}

			if (flatPoints.Count == 0)
			{
				Results.Rent(0);
				return;
			}

			float maxTerrainSlope = math.clamp(GetInputValue(nameof(Data.MaxTerrainSlopeDegrees), Data.MaxTerrainSlopeDegrees), 0f, 89f);
			float tiltReductionFactor = math.max(1f, GetInputValue(nameof(Data.TiltReductionFactor), Data.TiltReductionFactor));
			float rootRadius = math.max(0f, GetInputValue(nameof(Data.RootRadius), Data.RootRadius));
			float maxSink = math.max(0f, GetInputValue(nameof(Data.MaxSink), Data.MaxSink));
			float maxTerrainSlopeRadians = math.radians(maxTerrainSlope);

			var (keepMask, stabilizedPoints) = await PcgWorkerScheduler.RunAsync(() =>
			{
				var keep = new bool[flatPoints.Count];
				var stabilized = new PointData[flatPoints.Count];
				for (int i = 0; i < flatPoints.Count; i++)
				{
					ct.ThrowIfCancellationRequested();
					var point = flatPoints[i];
					var normal = (float3)point.Normal;
					float lengthSq = math.lengthsq(normal);
					if (lengthSq < 1e-8f)
					{
						point.Normal = Vector3.up;
						keep[i] = true;
						stabilized[i] = point;
						continue;
					}

					normal *= math.rsqrt(lengthSq);
					if (normal.y < 0f)
						normal = -normal;
					float upDot = math.clamp(normal.y, 0f, 1f);
					float originalTilt = math.acos(upDot);
					if (originalTilt > maxTerrainSlopeRadians)
					{
						keep[i] = false;
						continue;
					}

					float stabilizedTilt = originalTilt / tiltReductionFactor;
					float stabilizedTiltSin = math.sin(stabilizedTilt);
					float stabilizedTiltCos = math.cos(stabilizedTilt);
					var horizontal = new float2(normal.x, normal.z);
					float horizontalLengthSq = math.lengthsq(horizontal);
					if (horizontalLengthSq < 1e-8f)
						point.Normal = Vector3.up;
					else
					{
						horizontal *= math.rsqrt(horizontalLengthSq);
						point.Normal = new Vector3(
							horizontal.x * stabilizedTiltSin,
							stabilizedTiltCos,
							horizontal.y * stabilizedTiltSin);
					}

					float scale = math.max(0f, point.Scale);
					float sink = rootRadius * scale * math.max(0f, math.sin(originalTilt) - stabilizedTiltSin);
					point.Position -= new float3(0f, math.min(maxSink, sink), 0f);

					keep[i] = true;
					stabilized[i] = point;
				}
				return (keep, stabilized);
			}, ct);

			int keptCount = 0;
			for (int i = 0; i < keepMask.Length; i++)
			{
				if (keepMask[i])
					keptCount++;
			}

			Results.Rent(keptCount);
			for (int i = 0; i < flatPoints.Count; i++)
			{
				if (keepMask[i])
					Results.Value.AppendFrom(flatClouds[i], flatIndices[i], stabilizedPoints[i]);
			}
		}

		public override void DrawPreview(Transform transform)
		{
			GizmosUtility.DrawPoints(this, Results.Value, GetGizmosOptions(), transform);
		}
	}
}
