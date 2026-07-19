using System.Linq;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public sealed class SplineSnapshot
	{
		public BezierKnot[] Knots;
		public TangentMode[] Modes;
		public float[] Tensions;
		public BezierCurve[] Curves;
		public float[] CurveLengths;
		public float[] PrefixLengths;
		public bool Closed;
		public float Length;
		public bool HasEmbeddedData;

		public int CurveCount => Curves.Length;

		public static SplineSnapshot Capture(Spline spline)
		{
			var count = spline.Count;
			var closed = spline.Closed;
			var curveCount = closed ? count : count - 1;
			if (curveCount < 0)
				curveCount = 0;

			var snapshot = new SplineSnapshot
			{
				Closed = closed,
				Knots = new BezierKnot[count],
				Modes = new TangentMode[count],
				Tensions = new float[count],
				Curves = new BezierCurve[curveCount],
				CurveLengths = new float[curveCount],
				PrefixLengths = new float[curveCount]
			};

			for (int i = 0; i < count; i++)
			{
				snapshot.Knots[i] = spline[i];
				snapshot.Modes[i] = spline.GetTangentMode(i);
				snapshot.Tensions[i] = spline.GetAutoSmoothTension(i);
			}

			float accumulated = 0f;
			for (int i = 0; i < curveCount; i++)
			{
				snapshot.Curves[i] = spline.GetCurve(i);
				snapshot.CurveLengths[i] = spline.GetCurveLength(i);
				snapshot.PrefixLengths[i] = accumulated;
				accumulated += snapshot.CurveLengths[i];
			}

			snapshot.Length = accumulated;
			snapshot.HasEmbeddedData =
				spline.GetFloatDataKeys().Any() ||
				spline.GetFloat4DataKeys().Any() ||
				spline.GetIntDataKeys().Any() ||
				spline.GetObjectDataKeys().Any();

			return snapshot;
		}
	}
}
