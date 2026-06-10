using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Points;
using PCG.GraphModel;

namespace PCG.Splines
{
	[Serializable]
	public class RandomSplineNode : PcgPreviewNode
	{
		[Output] public List<Spline> Results => default;

		[Input] public List<PointData> Points = new();

		[Input] public Vector3 Up = new(0f, 1f, 0f);

		[Input] public int Segments = 10;

		[Input] public Vector2 Height = new Vector2(3f, 5f);

		[Input] public int Seed = 0;
	}
}
