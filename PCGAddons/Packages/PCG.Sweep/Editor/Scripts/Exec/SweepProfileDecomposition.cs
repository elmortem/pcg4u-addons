using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepProfileDecomposition
	{
		internal sealed class Chain
		{
			public int[] Points;
			public int Class;
			public float XMin;
			public float XMax;
		}

		internal sealed class Wall
		{
			public int[] Points;
		}

		public List<Chain> Chains = new List<Chain>();
		public List<Wall> Walls = new List<Wall>();
		public bool Closed;

		private const int ClassR = 1;
		private const int ClassL = -1;
		private const int ClassV = 0;

		internal static SweepProfileDecomposition Build(float2[] points, int[] segments, bool closed)
		{
			var result = new SweepProfileDecomposition { Closed = closed };

			int edgeCount = segments.Length / 2;
			if (edgeCount == 0)
				return result;

			float maxAbsX = 1e-4f;
			for (int i = 0; i < points.Length; i++)
				maxAbsX = math.max(maxAbsX, math.abs(points[i].x));
			float epsX = 1e-4f * maxAbsX;

			var cls = new int[edgeCount];
			for (int k = 0; k < edgeCount; k++)
			{
				int a = segments[2 * k];
				int b = segments[2 * k + 1];
				float dx = points[b].x - points[a].x;
				cls[k] = dx > epsX ? ClassR : (dx < -epsX ? ClassL : ClassV);
			}

			var runs = GroupRuns(cls, edgeCount, closed);

			for (int r = 0; r < runs.Count; r++)
			{
				var run = runs[r];
				var pts = RunPoints(segments, run);
				if (pts.Count < 2)
					continue;

				if (cls[run[0]] == ClassV)
				{
					pts.Sort((x, y) => points[x].y.CompareTo(points[y].y));
					result.Walls.Add(new Wall { Points = pts.ToArray() });
				}
				else
				{
					if (cls[run[0]] == ClassL)
						pts.Reverse();

					float xmin = float.MaxValue;
					float xmax = float.MinValue;
					for (int i = 0; i < pts.Count; i++)
					{
						xmin = math.min(xmin, points[pts[i]].x);
						xmax = math.max(xmax, points[pts[i]].x);
					}

					result.Chains.Add(new Chain
					{
						Points = pts.ToArray(),
						Class = cls[run[0]],
						XMin = xmin,
						XMax = xmax
					});
				}
			}

			return result;
		}

		private static List<List<int>> GroupRuns(int[] cls, int edgeCount, bool closed)
		{
			var runs = new List<List<int>>();
			var current = new List<int> { 0 };
			for (int k = 1; k < edgeCount; k++)
			{
				if (cls[k] == cls[k - 1])
				{
					current.Add(k);
				}
				else
				{
					runs.Add(current);
					current = new List<int> { k };
				}
			}
			runs.Add(current);

			if (closed && runs.Count > 1)
			{
				var first = runs[0];
				var last = runs[runs.Count - 1];
				if (cls[first[0]] == cls[last[0]])
				{
					last.AddRange(first);
					runs[0] = last;
					runs.RemoveAt(runs.Count - 1);
				}
			}

			return runs;
		}

		private static List<int> RunPoints(int[] segments, List<int> run)
		{
			var pts = new List<int>();
			for (int i = 0; i < run.Count; i++)
			{
				int edge = run[i];
				int a = segments[2 * edge];
				int b = segments[2 * edge + 1];
				if (pts.Count == 0)
					pts.Add(a);
				else if (pts[pts.Count - 1] != a)
					pts.Add(a);
				pts.Add(b);
			}
			return pts;
		}
	}
}
