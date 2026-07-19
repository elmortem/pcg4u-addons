using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines.Tools
{
	public static class SplineCopyUtility
	{
		public static Spline CopySpline(Spline source)
		{
			return CopySpline(source, float4x4.identity);
		}

		public static Spline CopySpline(Spline source, float4x4 matrix)
		{
			int count = source.Count;
			var knots = new BezierKnot[count];
			bool identity = matrix.Equals(float4x4.identity);
			for (int i = 0; i < count; i++)
				knots[i] = identity ? source[i] : source[i].Transform(matrix);

			var result = new Spline(knots, source.Closed);
			for (int i = 0; i < count; i++)
			{
				result.SetTangentModeNoNotify(i, source.GetTangentMode(i));
				result.SetAutoSmoothTensionNoNotify(i, source.GetAutoSmoothTension(i));
			}

			CopyEmbeddedData(source, result);
			return result;
		}

		public static List<Spline> CopySplines(IReadOnlyList<Spline> source)
		{
			var result = new List<Spline>(source != null ? source.Count : 0);
			if (source == null)
				return result;

			foreach (var spline in source)
			{
				if (spline == null)
					continue;

				result.Add(CopySpline(spline));
			}

			return result;
		}

		public static KnotLinkCollection CopyLinks(KnotLinkCollection source)
		{
			var copy = new KnotLinkCollection();
			RestoreLinks(source, copy);
			return copy;
		}

		public static void RestoreLinks(KnotLinkCollection source, KnotLinkCollection target)
		{
			if (target == null)
				return;

			target.Clear();
			if (source == null || source.Count == 0)
				return;

			var json = JsonUtility.ToJson(source);
			if (!string.IsNullOrEmpty(json))
				JsonUtility.FromJsonOverwrite(json, target);
		}

		private static void CopyEmbeddedData(Spline source, Spline target)
		{
			foreach (var key in source.GetFloatDataKeys())
			{
				if (source.TryGetFloatData(key, out var data))
					target.SetFloatData(key, data);
			}

			foreach (var key in source.GetFloat4DataKeys())
			{
				if (source.TryGetFloat4Data(key, out var data))
					target.SetFloat4Data(key, data);
			}

			foreach (var key in source.GetIntDataKeys())
			{
				if (source.TryGetIntData(key, out var data))
					target.SetIntData(key, data);
			}

			foreach (var key in source.GetObjectDataKeys())
			{
				if (source.TryGetObjectData(key, out var data))
					target.SetObjectData(key, data);
			}
		}
	}
}
