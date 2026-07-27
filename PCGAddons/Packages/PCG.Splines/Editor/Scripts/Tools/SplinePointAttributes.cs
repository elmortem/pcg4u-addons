using System.Collections.Generic;
using PCG.Points;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public static class SplinePointAttributes
	{
		public static PcgPointCloud Build(
			List<PointData> points,
			List<float> times,
			List<float> distances,
			List<PcgSplineSet> sourceSets,
			List<int> sourceRows,
			List<Spline> sourceSplines,
			List<int> sourceSplineIndices)
		{
			var cloud = new PcgPointCloud(points.Count);
			for (int i = 0; i < points.Count; i++)
			{
				cloud.Points.Add(points[i]);
				cloud.Attributes.AppendRow(sourceSets[i].Attributes, sourceRows[i]);
			}

			var indexColumn = cloud.Attributes.EnsureColumn<int>(SplineAttributes.SplineIndex);
			var timeColumn = cloud.Attributes.EnsureColumn<float>(SplineAttributes.SplineT);
			var distanceColumn = cloud.Attributes.EnsureColumn<float>(SplineAttributes.SplineDistance);
			var widthColumn = cloud.Attributes.EnsureColumn<float>(SplineAttributes.SplineWidth);
			for (int i = 0; i < points.Count; i++)
			{
				indexColumn.Values[i] = sourceSplineIndices[i];
				timeColumn.Values[i] = times[i];
				distanceColumn.Values[i] = distances[i];
				widthColumn.Values[i] = times[i] < 0f ? 0f : SplineWidthUtility.Evaluate(sourceSplines[i], times[i], 0f);
			}

			return cloud;
		}
	}
}
