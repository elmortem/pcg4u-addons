using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	internal static class SweepRibbonSampling
	{
		private const float MaxTurnPerSampleDeg = 8f;
		private const int MaxSubdivisions = 64;

		internal static float2 PlanRight(float3 tangent, float3 up, float3 prevPos, float3 nextPos)
		{
			float3 right = math.cross(up, tangent);
			float2 planRight = new float2(right.x, right.z);
			if (math.lengthsq(planRight) > 1e-10f)
				return math.normalize(planRight);

			float2 planTangent = math.normalizesafe(new float2(nextPos.x - prevPos.x, nextPos.z - prevPos.z), new float2(0f, 1f));
			return new float2(planTangent.y, -planTangent.x);
		}

		internal static List<float> AdaptiveStations(Spline spline, float start, float end, float baseStep)
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
				float turn = math.acos(math.clamp(math.dot(baseTan[c], baseTan[c + 1]), -1f, 1f));
				int sub = math.clamp((int)math.ceil(turn / maxTurnRad), 1, MaxSubdivisions);
				for (int s = 1; s <= sub; s++)
					dists.Add(math.lerp(baseDist[c], baseDist[c + 1], s / (float)sub));
			}

			return dists;
		}
	}
}
