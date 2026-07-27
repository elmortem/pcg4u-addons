using System.Collections.Generic;
using PCG.Points;
using PCG.Splines;

namespace PCG.CreatePoints
{
	public sealed class OffsetPointBuffer
	{
		public readonly List<PointData> Points = new();
		public readonly List<float> Times = new();
		public readonly List<float> Distances = new();
		public readonly List<float> Widths = new();
		public readonly List<int> Sides = new();
		public readonly List<PcgSplineSet> SourceSets = new();
		public readonly List<int> SourceRows = new();
		public readonly List<int> SourceSplineIndices = new();

		public void FillSource(int start, PcgSplineSet set, int row, int splineIndex)
		{
			for (int i = start; i < Points.Count; i++)
			{
				SourceSets.Add(set);
				SourceRows.Add(row);
				SourceSplineIndices.Add(splineIndex);
			}
		}

		public PcgPointCloud BuildCloud(bool withSplineMetrics)
		{
			var cloud = new PcgPointCloud(Points.Count);
			for (int i = 0; i < Points.Count; i++)
			{
				cloud.Points.Add(Points[i]);
				cloud.Attributes.AppendRow(SourceSets[i].Attributes, SourceRows[i]);
			}

			var indexColumn = cloud.Attributes.EnsureColumn<int>(SplineAttributes.SplineIndex);
			for (int i = 0; i < Points.Count; i++)
			{
				indexColumn.Values[i] = SourceSplineIndices[i];
			}

			if (!withSplineMetrics)
				return cloud;

			var timeColumn = cloud.Attributes.EnsureColumn<float>(SplineAttributes.SplineT);
			var distanceColumn = cloud.Attributes.EnsureColumn<float>(SplineAttributes.SplineDistance);
			var widthColumn = cloud.Attributes.EnsureColumn<float>(SplineAttributes.SplineWidth);
			var sideColumn = cloud.Attributes.EnsureColumn<int>(SplineAttributes.SplineSide);
			for (int i = 0; i < Points.Count; i++)
			{
				timeColumn.Values[i] = Times[i];
				distanceColumn.Values[i] = Distances[i];
				widthColumn.Values[i] = Widths[i];
				sideColumn.Values[i] = Sides[i];
			}

			return cloud;
		}
	}
}
