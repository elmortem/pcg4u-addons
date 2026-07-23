using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine.Splines;

namespace PCG.Splines.Tools
{
	internal static class SplineResampleUtility
	{
		public static async UniTask<Spline> ResampleAsync(Spline spline, float step, OperationScope scope, CancellationToken ct)
		{
			float length = spline.GetLength();
			int steps = math.max(1, (int)math.round(length / math.max(0.0001f, step)));
			float arcStep = length / steps;
			int lastIndex = spline.Closed ? steps - 1 : steps;

			var result = new Spline
			{
				Closed = spline.Closed
			};

			for (int i = 0; i <= lastIndex; i++)
			{
				float t = SplineUtility.ConvertIndexUnit(spline, i * arcStep, PathIndexUnit.Distance, PathIndexUnit.Normalized);
				float3 position = spline.EvaluatePosition(math.clamp(t, 0f, 1f));
				result.Add(new BezierKnot(position, float3.zero, float3.zero), TangentMode.AutoSmooth);
				await scope.Step(ct: ct);
			}

			return result;
		}
	}
}
