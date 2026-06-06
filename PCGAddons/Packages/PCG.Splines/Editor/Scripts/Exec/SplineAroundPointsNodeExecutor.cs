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
	public class SplineAroundPointsNodeExecutor : PcgAsyncPreviewNodeExecutor<SplineAroundPointsNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<Spline>();

			var pointsList = GetInputValues(nameof(Data.Points), Data.Points);
			if (pointsList == null || pointsList.Length <= 0)
				return;

			var radius = GetInputValue(nameof(Data.Radius), Data.Radius);
			var pointsCount = GetInputValue(nameof(Data.PointsCount), Data.PointsCount);
			var up = GetInputValue(nameof(Data.Up), Data.Up);
			var seed = GetInputValue(nameof(Data.Seed), Data.Seed);

			if (seed == -1)
				seed = UnityEngine.Random.Range(1, int.MaxValue);

			RandomUtility.PushSeed(seed);

			using (var scope = OperationScope.Start(this))
			{
				foreach (var points in pointsList)
				{
					if (points == null)
						continue;

					foreach (var point in points)
					{
						var spline = new Spline
						{
							Closed = true
						};

						var angleStep = 2f * Mathf.PI / pointsCount;
						var right = math.normalize(math.cross(up, Vector3.forward));
						var forward = math.normalize(math.cross(right, up));

						for (int i = 0; i < pointsCount; i++)
						{
							var angle = angleStep * i;
							var currentRadius = RandomUtility.Range(radius);

							var offset = right * (math.cos(angle) * currentRadius) +
									   forward * (math.sin(angle) * currentRadius);

							var position = point.Position + offset;

							spline.Add(new BezierKnot(position, float3.zero, float3.zero),
								TangentMode.AutoSmooth);

							await scope.Step(ct: ct);
						}

						Results.Value.Add(spline);
					}
				}
			}

			RandomUtility.PopSeed();
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
		}
	}
}
