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
		public PcgOutput<List<PointData>> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length == 0)
				return;

			var results = new List<PointData>(pointsList.TotalCount());
			foreach (var points in pointsList)
			{
				if (points != null)
					results.AddRange(points);
			}

			float maxTerrainSlope = math.clamp(GetInputValue(nameof(Data.MaxTerrainSlopeDegrees), Data.MaxTerrainSlopeDegrees), 0f, 89f);
			float tiltReductionFactor = math.max(1f, GetInputValue(nameof(Data.TiltReductionFactor), Data.TiltReductionFactor));
			float rootRadius = math.max(0f, GetInputValue(nameof(Data.RootRadius), Data.RootRadius));
			float maxSink = math.max(0f, GetInputValue(nameof(Data.MaxSink), Data.MaxSink));
			float maxTerrainSlopeRadians = math.radians(maxTerrainSlope);

			var computed = await PcgWorkerScheduler.RunAsync(() =>
			{
				var output = new List<PointData>(results);
				for (int i = output.Count - 1; i >= 0; i--)
				{
					ct.ThrowIfCancellationRequested();
					var point = output[i];
					var normal = (float3)point.Normal;
					float lengthSq = math.lengthsq(normal);
					if (lengthSq < 1e-8f)
					{
						point.Normal = Vector3.up;
						output[i] = point;
						continue;
					}

					normal *= math.rsqrt(lengthSq);
					if (normal.y < 0f)
						normal = -normal;
					float upDot = math.clamp(normal.y, 0f, 1f);
					float originalTilt = math.acos(upDot);
					if (originalTilt > maxTerrainSlopeRadians)
					{
						output.RemoveAt(i);
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
					output[i] = point;
				}
				return output;
			}, ct);

			Results.Value = computed;
		}

		public override void DrawPreview(Transform transform)
		{
			GizmosUtility.DrawPoints(Results.Value, GetGizmosOptions(), transform);
		}
	}
}
