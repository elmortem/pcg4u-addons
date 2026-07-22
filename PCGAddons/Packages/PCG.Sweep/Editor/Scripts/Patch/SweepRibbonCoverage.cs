using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepRibbonCoverage
	{
		private const int MaxCellsPerQuad = 1024;

		private struct Quad
		{
			public int Spline;
			public float Station;
			public float2 A;
			public float2 B;
			public float2 C;
			public float2 D;
		}

		private readonly List<Quad> _quads = new();
		private readonly Dictionary<long, List<int>> _cells = new();
		private readonly List<int> _large = new();
		private readonly float _cellSize;

		internal SweepRibbonCoverage(List<SweepBoundaryCurve> curves, float cellSize)
		{
			_cellSize = math.max(0.05f, cellSize);

			for (int c = 0; c < curves.Count; c += 2)
			{
				var left = curves[c];
				var right = curves[c + 1];
				int count = math.min(left.Plan.Length, right.Plan.Length);

				for (int r = 0; r + 1 < count; r++)
				{
					var quad = new Quad
					{
						Spline = left.SplineIndex,
						Station = (left.Station[r] + left.Station[r + 1]) * 0.5f,
						A = left.Plan[r],
						B = left.Plan[r + 1],
						C = right.Plan[r + 1],
						D = right.Plan[r]
					};

					if (Degenerate(quad))
						continue;

					_quads.Add(quad);
				}
			}

			for (int q = 0; q < _quads.Count; q++)
				Insert(q);
		}

		internal bool IsCovered(float2 point, int excludeSpline, float excludeStation, float stationGuard)
		{
			for (int i = 0; i < _large.Count; i++)
			{
				if (Hits(_large[i], point, excludeSpline, excludeStation, stationGuard))
					return true;
			}

			int x = (int)math.floor(point.x / _cellSize);
			int y = (int)math.floor(point.y / _cellSize);
			long key = ((long)x << 32) ^ (uint)y;
			if (!_cells.TryGetValue(key, out var list))
				return false;

			for (int i = 0; i < list.Count; i++)
			{
				if (Hits(list[i], point, excludeSpline, excludeStation, stationGuard))
					return true;
			}

			return false;
		}

		private bool Hits(int index, float2 point, int excludeSpline, float excludeStation, float stationGuard)
		{
			Quad quad = _quads[index];
			if (quad.Spline == excludeSpline && math.abs(quad.Station - excludeStation) <= stationGuard)
				return false;

			return PointInTriangle(point, quad.A, quad.B, quad.C) || PointInTriangle(point, quad.A, quad.C, quad.D);
		}

		private static bool Degenerate(Quad quad)
		{
			float2 min = math.min(math.min(quad.A, quad.B), math.min(quad.C, quad.D));
			float2 max = math.max(math.max(quad.A, quad.B), math.max(quad.C, quad.D));
			return math.all(max - min < 1e-7f);
		}

		private void Insert(int index)
		{
			Quad quad = _quads[index];
			float2 min = math.min(math.min(quad.A, quad.B), math.min(quad.C, quad.D));
			float2 max = math.max(math.max(quad.A, quad.B), math.max(quad.C, quad.D));

			int x0 = (int)math.floor(min.x / _cellSize);
			int x1 = (int)math.floor(max.x / _cellSize);
			int y0 = (int)math.floor(min.y / _cellSize);
			int y1 = (int)math.floor(max.y / _cellSize);

			long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
			if (cells > MaxCellsPerQuad)
			{
				_large.Add(index);
				return;
			}

			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					long key = ((long)x << 32) ^ (uint)y;
					if (!_cells.TryGetValue(key, out var list))
					{
						list = new List<int>();
						_cells.Add(key, list);
					}
					list.Add(index);
				}
			}
		}

		private static bool PointInTriangle(float2 p, float2 a, float2 b, float2 c)
		{
			float d1 = Cross(b - a, p - a);
			float d2 = Cross(c - b, p - b);
			float d3 = Cross(a - c, p - c);
			bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
			bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
			return !(hasNegative && hasPositive);
		}

		private static float Cross(float2 a, float2 b)
		{
			return a.x * b.y - a.y * b.x;
		}
	}
}
