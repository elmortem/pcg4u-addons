using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	internal sealed class SweepRibbonPath
	{
		internal float Length;
		internal float[] Stations;
		internal float[] NormalizedTs;
		internal float3[] Positions;
		internal float3[] Tangents;
		internal float3[] Ups;

		internal int Count => Stations != null ? Stations.Length : 0;
	}

	internal static class SweepRibbonSampling
	{
		private const float MaxTurnPerSampleDeg = 8f;
		private const int MaxSubdivisions = 64;
		private const float MinPlanFractionSq = 1e-2f;
		private const float MinDirectionLengthSq = 1e-10f;

		internal static float3 Right3D(float3 tangent, float3 up, float3 prevPos, float3 nextPos)
		{
			float3 right = math.cross(up, tangent);
			// Merge topology lives in XZ: keep the old plan width while retaining the frame's lateral height slope.
			if (TryNormalizePlanRight(right, out float3 normalizedRight))
				return normalizedRight;

			float3 fallbackTangent = nextPos - prevPos;
			right = math.cross(up, fallbackTangent);
			if (TryNormalizePlanRight(right, out normalizedRight))
				return normalizedRight;

			float2 planTangent = math.normalizesafe(new float2(fallbackTangent.x, fallbackTangent.z), new float2(0f, 1f));
			return new float3(planTangent.y, 0f, -planTangent.x);
		}

		private static bool TryNormalizePlanRight(float3 right, out float3 normalizedRight)
		{
			normalizedRight = default;
			float lengthSq = math.lengthsq(right);
			float planLengthSq = right.x * right.x + right.z * right.z;
			// A nearly vertical lateral axis cannot represent a stable single-valued 2.5D ribbon.
			if (!math.isfinite(lengthSq) || !math.isfinite(planLengthSq) ||
				lengthSq <= MinDirectionLengthSq || planLengthSq < lengthSq * MinPlanFractionSq)
				return false;

			normalizedRight = right * math.rsqrt(planLengthSq);
			return math.all(math.isfinite(normalizedRight));
		}

		internal static SweepRibbonPath Capture(Spline spline, float start, float end, float baseStep, int minSamples, CancellationToken ct)
		{
			// Unity Splines may allocate Allocator.Temp while evaluating up vectors, so capture only on the editor thread.
			float length = spline.GetLength();
			if (!(length > 1e-4f) || !math.isfinite(length))
				return null;

			float rangeStart = math.clamp(start, 0f, length);
			float rangeEnd = math.clamp(end, 0f, length);
			if (!(rangeEnd - rangeStart > 1e-4f))
				return null;

			var stations = AdaptiveStations(spline, rangeStart, rangeEnd, baseStep, ct);
			minSamples = math.max(2, minSamples);
			if (stations.Count < minSamples)
			{
				stations.Clear();
				for (int i = 0; i < minSamples; i++)
					stations.Add(math.lerp(rangeStart, rangeEnd, i / (float)(minSamples - 1)));
			}

			int count = stations.Count;
			var stationArray = stations.ToArray();
			var ts = new float[count];
			var positions = new float3[count];
			var tangents = new float3[count];
			var ups = new float3[count];
			for (int i = 0; i < count; i++)
			{
				ct.ThrowIfCancellationRequested();
				float t = math.saturate(spline.ConvertIndexUnit(stationArray[i], PathIndexUnit.Distance, PathIndexUnit.Normalized));
				float3 position = spline.EvaluatePosition(t);
				float3 tangent = spline.EvaluateTangent(t);
				float3 up = spline.EvaluateUpVector(t);
				if (!math.all(math.isfinite(position)) || !math.all(math.isfinite(tangent)) || !math.all(math.isfinite(up)))
					return null;

				ts[i] = t;
				positions[i] = position;
				tangents[i] = tangent;
				ups[i] = up;
			}

			return new SweepRibbonPath
			{
				Length = length,
				Stations = stationArray,
				NormalizedTs = ts,
				Positions = positions,
				Tangents = tangents,
				Ups = ups
			};
		}

		internal static async UniTask<SweepRibbonPath> CaptureAsync(
			Spline spline,
			float start,
			float end,
			float baseStep,
			int minSamples,
			OperationScope scope,
			CancellationToken ct)
		{
			float length = spline.GetLength();
			if (!(length > 1e-4f) || !math.isfinite(length))
				return null;

			float rangeStart = math.clamp(start, 0f, length);
			float rangeEnd = math.clamp(end, 0f, length);
			if (!(rangeEnd - rangeStart > 1e-4f))
				return null;

			var stations = await AdaptiveStationsAsync(spline, rangeStart, rangeEnd, baseStep, scope, ct);
			minSamples = math.max(2, minSamples);
			if (stations.Count < minSamples)
			{
				stations.Clear();
				for (int i = 0; i < minSamples; i++)
					stations.Add(math.lerp(rangeStart, rangeEnd, i / (float)(minSamples - 1)));
			}

			int count = stations.Count;
			var stationArray = stations.ToArray();
			var ts = new float[count];
			var positions = new float3[count];
			var tangents = new float3[count];
			var ups = new float3[count];
			for (int i = 0; i < count; i++)
			{
				ct.ThrowIfCancellationRequested();
				float t = math.saturate(spline.ConvertIndexUnit(stationArray[i], PathIndexUnit.Distance, PathIndexUnit.Normalized));
				float3 position = spline.EvaluatePosition(t);
				float3 tangent = spline.EvaluateTangent(t);
				float3 up = spline.EvaluateUpVector(t);
				if (!math.all(math.isfinite(position)) || !math.all(math.isfinite(tangent)) || !math.all(math.isfinite(up)))
					return null;

				ts[i] = t;
				positions[i] = position;
				tangents[i] = tangent;
				ups[i] = up;
				await scope.Step(ct: ct);
			}

			return new SweepRibbonPath
			{
				Length = length,
				Stations = stationArray,
				NormalizedTs = ts,
				Positions = positions,
				Tangents = tangents,
				Ups = ups
			};
		}

		internal static List<float> AdaptiveStations(Spline spline, float start, float end, float baseStep, CancellationToken ct)
		{
			var dists = new List<float>();
			float span = end - start;
			if (!(span > 1e-4f))
			{
				dists.Add(start);
				return dists;
			}

			int coarse = math.max(1, (int)math.ceil(span / baseStep));
			var baseDist = new float[coarse + 1];
			var baseTan = new float2[coarse + 1];
			for (int c = 0; c <= coarse; c++)
			{
				ct.ThrowIfCancellationRequested();
				float d = start + span * c / coarse;
				baseDist[c] = d;
				float t = math.saturate(spline.ConvertIndexUnit(d, PathIndexUnit.Distance, PathIndexUnit.Normalized));
				float3 tangent = spline.EvaluateTangent(t);
				baseTan[c] = math.normalizesafe(new float2(tangent.x, tangent.z), new float2(0f, 1f));
			}

			float maxTurnRad = math.radians(MaxTurnPerSampleDeg);
			dists.Add(start);
			for (int c = 0; c < coarse; c++)
			{
				ct.ThrowIfCancellationRequested();
				float turn = math.acos(math.clamp(math.dot(baseTan[c], baseTan[c + 1]), -1f, 1f));
				int sub = math.clamp((int)math.ceil(turn / maxTurnRad), 1, MaxSubdivisions);
				for (int s = 1; s <= sub; s++)
					dists.Add(math.lerp(baseDist[c], baseDist[c + 1], s / (float)sub));
			}

			return dists;
		}

		private static async UniTask<List<float>> AdaptiveStationsAsync(
			Spline spline,
			float start,
			float end,
			float baseStep,
			OperationScope scope,
			CancellationToken ct)
		{
			var dists = new List<float>();
			float span = end - start;
			if (!(span > 1e-4f))
			{
				dists.Add(start);
				return dists;
			}

			int coarse = math.max(1, (int)math.ceil(span / baseStep));
			var baseDist = new float[coarse + 1];
			var baseTan = new float2[coarse + 1];
			for (int c = 0; c <= coarse; c++)
			{
				ct.ThrowIfCancellationRequested();
				float d = start + span * c / coarse;
				baseDist[c] = d;
				float t = math.saturate(spline.ConvertIndexUnit(d, PathIndexUnit.Distance, PathIndexUnit.Normalized));
				float3 tangent = spline.EvaluateTangent(t);
				baseTan[c] = math.normalizesafe(new float2(tangent.x, tangent.z), new float2(0f, 1f));
				await scope.Step(ct: ct);
			}

			float maxTurnRad = math.radians(MaxTurnPerSampleDeg);
			dists.Add(start);
			for (int c = 0; c < coarse; c++)
			{
				ct.ThrowIfCancellationRequested();
				float turn = math.acos(math.clamp(math.dot(baseTan[c], baseTan[c + 1]), -1f, 1f));
				int sub = math.clamp((int)math.ceil(turn / maxTurnRad), 1, MaxSubdivisions);
				for (int s = 1; s <= sub; s++)
					dists.Add(math.lerp(baseDist[c], baseDist[c + 1], s / (float)sub));
				await scope.Step(ct: ct);
			}

			return dists;
		}
	}
}
