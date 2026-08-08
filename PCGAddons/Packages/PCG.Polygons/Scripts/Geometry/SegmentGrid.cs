using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed class SegmentGrid
	{
		public struct Segment
		{
			public float2 A;
			public float2 B;
			public float2 Min;
			public float2 Max;
		}

		public float2 Origin;
		public float CellSize;
		public int Cols;
		public int Rows;
		public Segment[] Segments;

		private List<int>[] _cells;
		private int[] _stamp;
		private int _stampGeneration;

		public static SegmentGrid Build(IList<Polygon2D> merged, float2 origin, float cellSize, int cols, int rows)
		{
			var grid = new SegmentGrid();
			grid.Origin = origin;
			grid.CellSize = cellSize;
			grid.Cols = math.max(cols, 1);
			grid.Rows = math.max(rows, 1);

			var segments = new List<Segment>();
			for (int i = 0; i < merged.Count; i++)
			{
				var poly = merged[i];
				CollectRing(segments, poly.Outer);
				for (int h = 0; h < poly.Holes.Count; h++)
					CollectRing(segments, poly.Holes[h]);
			}

			grid.Segments = segments.ToArray();
			grid._stamp = new int[grid.Segments.Length];
			grid._cells = new List<int>[grid.Cols * grid.Rows];

			for (int i = 0; i < grid.Segments.Length; i++)
			{
				var s = grid.Segments[i];
				grid.CellRange(s.Min, s.Max, out int x0, out int z0, out int x1, out int z1);
				for (int iz = z0; iz <= z1; iz++)
				{
					for (int ix = x0; ix <= x1; ix++)
					{
						int index = iz * grid.Cols + ix;
						var list = grid._cells[index];
						if (list == null)
						{
							list = new List<int>();
							grid._cells[index] = list;
						}

						list.Add(i);
					}
				}
			}

			return grid;
		}

		public void CollectCandidates(float2 min, float2 max, List<int> buffer)
		{
			buffer.Clear();
			_stampGeneration++;

			CellRange(min, max, out int x0, out int z0, out int x1, out int z1);
			for (int iz = z0; iz <= z1; iz++)
			{
				for (int ix = x0; ix <= x1; ix++)
				{
					var list = _cells[iz * Cols + ix];
					if (list == null)
						continue;

					for (int i = 0; i < list.Count; i++)
					{
						int id = list[i];
						if (_stamp[id] == _stampGeneration)
							continue;

						_stamp[id] = _stampGeneration;
						buffer.Add(id);
					}
				}
			}
		}

		private void CellRange(float2 min, float2 max, out int x0, out int z0, out int x1, out int z1)
		{
			x0 = math.clamp((int)math.floor((min.x - Origin.x) / CellSize), 0, Cols - 1);
			z0 = math.clamp((int)math.floor((min.y - Origin.y) / CellSize), 0, Rows - 1);
			x1 = math.clamp((int)math.floor((max.x - Origin.x) / CellSize), 0, Cols - 1);
			z1 = math.clamp((int)math.floor((max.y - Origin.y) / CellSize), 0, Rows - 1);
		}

		private static void CollectRing(List<Segment> segments, float2[] ring)
		{
			for (int i = 0; i < ring.Length; i++)
			{
				float2 a = ring[i];
				float2 b = ring[(i + 1) % ring.Length];
				segments.Add(new Segment
				{
					A = a,
					B = b,
					Min = math.min(a, b),
					Max = math.max(a, b)
				});
			}
		}
	}
}
