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
	public class RandomSplineNodeExecutor : PcgAsyncPreviewNodeExecutor<RandomSplineNode>
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
			Results.Value = new List<Spline>();

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var flatPoints = new List<PointData>();
			foreach (var points in pointsList)
			{
				if (points == null || points.Count <= 0)
					continue;

				flatPoints.AddRange(points);
			}

			if (flatPoints.Count <= 1)
				return;

			var up = GetInputValue(nameof(Data.Up), Data.Up);
			var segments = GetInputValue(nameof(Data.Segments), Data.Segments);
			var height = GetInputValue(nameof(Data.Height), Data.Height);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);

			var random = PcgRandom.Create(seed);

			using (var scope = OperationScope.Start(this))
			{
				for (int p = 0; p + 1 < flatPoints.Count; p += 2)
				{
					var startPoint = flatPoints[p].Position;
					var finishPoint = flatPoints[p + 1].Position;

					var spline = new Spline
					{
						Closed = false
					};

					spline.Add(new BezierKnot(startPoint, float3.zero, float3.zero), TangentMode.AutoSmooth);
					var dist = Vector3.Distance(startPoint, finishPoint);
					var step = dist / segments;
					var direction = (finishPoint - startPoint).Normalized();
					var perpDirection = math.cross(direction, up).Normalized();

					for (int i = 1; i < segments; i++)
					{
						var point = startPoint + direction * (step * i);

						var randomDistance = random.NextFloat(height.x, height.y);
						var randomOffset = perpDirection * randomDistance;
						if (random.NextFloat() > 0.5f)
						{
							randomOffset = -randomOffset;
						}

						point += randomOffset;

						spline.Add(new BezierKnot(point, float3.zero, float3.zero), TangentMode.AutoSmooth);

						await scope.Step(ct: ct);
					}

					spline.Add(new BezierKnot(finishPoint, float3.zero, float3.zero), TangentMode.AutoSmooth);

					Results.Value.Add(spline);
				}
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
