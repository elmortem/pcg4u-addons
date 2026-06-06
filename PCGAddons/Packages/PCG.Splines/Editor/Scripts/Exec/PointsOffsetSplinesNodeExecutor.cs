using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;
using PCG.Utilities;
using PCG.Exec;

namespace PCG.CreatePoints
{
	public class PointsOffsetSplinesNodeExecutor : PcgAsyncPreviewNodeExecutor<PointsOffsetSplinesNode>, IPointsCount
	{
		public PcgOutput<List<PointData>> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<PointData>();

			var offset = GetInputValue(nameof(Data.Offset), Data.Offset);
			if (math.abs(offset) < 0.0001f)
				return;

			var distance = GetInputValue(nameof(Data.Distance), Data.Distance);
			if (math.abs(distance) < 0.0001f)
				return;

			var splinesPort = GetInputPort(nameof(Data.Splines));
			var splinesList = splinesPort.GetInputValues();
			if (splinesList == null || splinesList.Length <= 0)
				return;

			using (var scope = OperationScope.Start(this))
			{
				foreach (List<Spline> splines in splinesList)
				{
					if (splines == null || splines.Count <= 0)
						continue;

					foreach (var spline in splines)
					{
						if (spline.Count <= 1)
							continue;

						var length = spline.GetLength();
						var step = distance / length;
						for (float t = 0; t <= 1f; t += step)
						{
							spline.Evaluate(t, out var point, out var tangent, out var upVector);

							var offsetDirection = math.normalize(math.cross(tangent, upVector));

							if (Data.BothSides)
							{
								AddPoint(point + offsetDirection * math.abs(offset), tangent, upVector);
								AddPoint(point - offsetDirection * math.abs(offset), tangent, upVector);
							}
							else
							{
								var offsetPoint = point + offsetDirection * offset;
								AddPoint(offsetPoint, tangent, upVector);
							}

							await scope.Step(ct: ct);
						}
					}
				}
			}
		}

		private void AddPoint(float3 pos, float3 tangent, float3 upVector)
		{
			Results.Value.Add(new PointData
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

			GizmosUtility.DrawPoints(Results.Value, gizmosOptions, transform);
		}
	}
}
