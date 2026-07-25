using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public static class SplineWidthUtility
	{
		public const string DataKey = "pcg.width";

		public static bool TryEvaluate(Spline spline, float normalizedT, out float width)
		{
			width = 0f;
			if (!spline.TryGetFloatData(DataKey, out var data) || data == null || data.Count == 0)
				return false;

			width = data.Evaluate(spline, math.clamp(normalizedT, 0f, 1f), PathIndexUnit.Normalized, InterpolatorUtility.LerpFloat);
			return math.isfinite(width);
		}

		public static float Evaluate(Spline spline, float normalizedT, float fallback)
		{
			return TryEvaluate(spline, normalizedT, out float width) ? width : fallback;
		}

		public static void SetConstant(Spline spline, float width)
		{
			var data = new SplineData<float>
			{
				PathIndexUnit = PathIndexUnit.Normalized,
				DefaultValue = width
			};
			data.Add(0f, width);
			spline.SetFloatData(DataKey, data);
		}

		public static void SetSamples(Spline spline, IReadOnlyList<float> normalizedTimes, IReadOnlyList<float> widths)
		{
			var data = new SplineData<float>
			{
				PathIndexUnit = PathIndexUnit.Normalized
			};

			int count = math.min(normalizedTimes.Count, widths.Count);
			for (int i = 0; i < count; i++)
				data.Add(math.clamp(normalizedTimes[i], 0f, 1f), widths[i]);

			if (count > 0)
				data.DefaultValue = widths[0];

			spline.SetFloatData(DataKey, data);
		}

		public static void Copy(Spline source, Spline target)
		{
			if (!source.TryGetFloatData(DataKey, out var sourceData) || sourceData == null)
				return;

			var targetData = new SplineData<float>
			{
				PathIndexUnit = sourceData.PathIndexUnit,
				DefaultValue = sourceData.DefaultValue
			};

			foreach (var point in sourceData)
				targetData.Add(point);

			target.SetFloatData(DataKey, targetData);
		}

		public static void CopyResampled(Spline source, Spline target, IReadOnlyList<float> normalizedTimes)
		{
			if (!source.TryGetFloatData(DataKey, out var data) || data == null || data.Count == 0)
				return;

			var widths = new float[normalizedTimes.Count];
			for (int i = 0; i < normalizedTimes.Count; i++)
				widths[i] = data.Evaluate(source, normalizedTimes[i], PathIndexUnit.Normalized, InterpolatorUtility.LerpFloat);

			SetSamples(target, normalizedTimes, widths);
		}
	}
}
