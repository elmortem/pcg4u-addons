using Cysharp.Threading.Tasks;
using PCG.Utilities;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	internal static class SweepNetworkFrames
	{
		internal static SweepFrame[] BuildRangeFrames(Spline spline, float rangeStart, float rangeEnd, float sourceLength, float sourceOffset, float step, float maxStep, float maxAngleRad, int vpr, int maxVertices)
		{
			float span = rangeEnd - rangeStart;
			if (span <= 1e-4f)
				return null;

			int quantCount = math.max(1, (int)math.ceil(span / step));

			if ((long)(quantCount + 1) * vpr > maxVertices)
			{
				Debug.LogError($"[Sweep Spline] A piece would build {(long)(quantCount + 1) * vpr} vertices which exceeds the {maxVertices} limit; it was skipped.");
				return null;
			}

			float length = sourceLength > 1e-6f ? sourceLength : span;

			var quantFrames = new SweepFrame[quantCount + 1];
			for (int q = 0; q <= quantCount; q++)
			{
				float distance = rangeStart + span * q / quantCount;
				if (!TryBuildFrame(spline, distance, rangeStart, sourceOffset, length, out quantFrames[q]))
					return null;
			}

			var turns = new float[quantCount];
			var rolls = new float[quantCount];
			for (int q = 0; q < quantCount; q++)
			{
				float3 t0 = math.normalizesafe(quantFrames[q].Tangent, new float3(0f, 0f, 1f));
				float3 t1 = math.normalizesafe(quantFrames[q + 1].Tangent, new float3(0f, 0f, 1f));
				turns[q] = math.acos(math.clamp(math.dot(t0, t1), -1f, 1f));

				float3 u0 = math.normalizesafe(quantFrames[q].Up, new float3(0f, 1f, 0f));
				float3 u1 = math.normalizesafe(quantFrames[q + 1].Up, new float3(0f, 1f, 0f));
				rolls[q] = math.acos(math.clamp(math.dot(u0, u1), -1f, 1f));
			}

			var frames = new System.Collections.Generic.List<SweepFrame>(quantCount + 1);
			frames.Add(quantFrames[0]);

			int current = 0;
			while (current < quantCount)
			{
				int next = current + 1;
				float turnSum = turns[current];
				float rollSum = rolls[current];
				while (next < quantCount)
				{
					float candidateTurn = turnSum + turns[next];
					float candidateRoll = rollSum + rolls[next];
					float candidateLength = quantFrames[next + 1].Distance - quantFrames[current].Distance;
					if (candidateTurn > maxAngleRad || candidateRoll > maxAngleRad || candidateLength > maxStep)
						break;
					turnSum = candidateTurn;
					rollSum = candidateRoll;
					next++;
				}

				frames.Add(quantFrames[next]);
				current = next;
			}

			return frames.ToArray();
		}

		internal static async UniTask<SweepFrame[]> BuildRangeFramesAsync(
			Spline spline,
			float rangeStart,
			float rangeEnd,
			float sourceLength,
			float sourceOffset,
			float step,
			float maxStep,
			float maxAngleRad,
			int vpr,
			int maxVertices,
			OperationScope scope,
			CancellationToken ct)
		{
			float span = rangeEnd - rangeStart;
			if (span <= 1e-4f)
				return null;

			int quantCount = math.max(1, (int)math.ceil(span / step));
			if ((long)(quantCount + 1) * vpr > maxVertices)
			{
				Debug.LogError($"[Sweep Spline] A piece would build {(long)(quantCount + 1) * vpr} vertices which exceeds the {maxVertices} limit; it was skipped.");
				return null;
			}

			float length = sourceLength > 1e-6f ? sourceLength : span;
			var quantFrames = new SweepFrame[quantCount + 1];
			for (int q = 0; q <= quantCount; q++)
			{
				float distance = rangeStart + span * q / quantCount;
				if (!TryBuildFrame(spline, distance, rangeStart, sourceOffset, length, out quantFrames[q]))
					return null;
				await scope.Step(ct: ct);
			}

			var turns = new float[quantCount];
			var rolls = new float[quantCount];
			for (int q = 0; q < quantCount; q++)
			{
				float3 t0 = math.normalizesafe(quantFrames[q].Tangent, new float3(0f, 0f, 1f));
				float3 t1 = math.normalizesafe(quantFrames[q + 1].Tangent, new float3(0f, 0f, 1f));
				turns[q] = math.acos(math.clamp(math.dot(t0, t1), -1f, 1f));

				float3 u0 = math.normalizesafe(quantFrames[q].Up, new float3(0f, 1f, 0f));
				float3 u1 = math.normalizesafe(quantFrames[q + 1].Up, new float3(0f, 1f, 0f));
				rolls[q] = math.acos(math.clamp(math.dot(u0, u1), -1f, 1f));
				await scope.Step(ct: ct);
			}

			var frames = new System.Collections.Generic.List<SweepFrame>(quantCount + 1) { quantFrames[0] };
			int current = 0;
			while (current < quantCount)
			{
				int next = current + 1;
				float turnSum = turns[current];
				float rollSum = rolls[current];
				while (next < quantCount)
				{
					float candidateTurn = turnSum + turns[next];
					float candidateRoll = rollSum + rolls[next];
					float candidateLength = quantFrames[next + 1].Distance - quantFrames[current].Distance;
					if (candidateTurn > maxAngleRad || candidateRoll > maxAngleRad || candidateLength > maxStep)
						break;
					turnSum = candidateTurn;
					rollSum = candidateRoll;
					next++;
				}

				frames.Add(quantFrames[next]);
				current = next;
				await scope.Step(ct: ct);
			}

			return frames.ToArray();
		}

		private static bool TryBuildFrame(Spline spline, float distance, float rangeStart, float sourceOffset, float sourceLength, out SweepFrame frame)
		{
			frame = default;
			float t = math.clamp(spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized), 0f, 1f);
			float3 position = spline.EvaluatePosition(t);
			float3 tangent = spline.EvaluateTangent(t);
			float3 up = spline.EvaluateUpVector(t);

			if (!math.all(math.isfinite(position)) || !math.all(math.isfinite(tangent)) || !math.all(math.isfinite(up)))
				return false;

			float localDistance = distance - rangeStart;

			frame = new SweepFrame
			{
				Position = position,
				Tangent = tangent,
				Up = up,
				T = math.saturate((sourceOffset + localDistance) / sourceLength),
				Distance = localDistance
			};
			return true;
		}
	}
}
