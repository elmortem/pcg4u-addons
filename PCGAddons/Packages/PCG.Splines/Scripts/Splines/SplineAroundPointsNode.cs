using System.Collections.Generic;
using PCG.Points;
using UnityEngine;
using UnityEngine.Splines;
using PCG.GraphModel;

namespace PCG.Splines
{
	public class SplineAroundPointsNode : PcgPreviewNode
	{
		[Output] public List<Spline> Results => default;

		[Input] public List<PointData> Points = new();

		[Input] public Vector2 Radius = new(0.5f, 1f);

		[Input] public int PointsCount = 4;

		[Input] public Vector3 Up = new(0f, 1f, 0f);

		[Input] public int Seed = -1;
	}
}
