using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed class MeshQuadtree
	{
		public float2 Origin;
		public float MaxCellSize;
		public float MinCellSize;
		public int MaxDepth;
		public Dictionary<(int, int, int), QuadLeaf> Leaves = new();

		private IList<Polygon2D> _merged;
		private List<(float2 A, float2 B, float2 Min, float2 Max)> _segments;
		private Func<float2, float> _sampleHeight;
		private float _maxHeightError;

		public float CellSize(int depth)
		{
			return MaxCellSize / (1 << depth);
		}

		public float2 CellMin(int depth, int ix, int iz)
		{
			float cs = CellSize(depth);
			return Origin + new float2(ix, iz) * cs;
		}

		public static MeshQuadtree Build(IList<Polygon2D> merged, float2 boundsMin, float2 boundsMax, float maxCellSize, float minCellSize, int maxDepth, Func<float2, float> sampleHeight, float maxHeightError)
		{
			var tree = new MeshQuadtree();
			tree.MaxCellSize = maxCellSize;
			tree.MinCellSize = math.clamp(minCellSize, 1e-3f, maxCellSize);
			tree.MaxDepth = maxDepth;
			tree._merged = merged;
			tree._sampleHeight = sampleHeight;
			tree._maxHeightError = maxHeightError;
			tree.Origin = new float2(math.floor(boundsMin.x / maxCellSize) * maxCellSize, math.floor(boundsMin.y / maxCellSize) * maxCellSize);

			tree._segments = new List<(float2, float2, float2, float2)>();
			for (int i = 0; i < merged.Count; i++)
			{
				var poly = merged[i];
				tree.CollectSegments(poly.Outer);
				for (int h = 0; h < poly.Holes.Count; h++)
					tree.CollectSegments(poly.Holes[h]);
			}

			int cols = (int)math.ceil((boundsMax.x - tree.Origin.x) / maxCellSize);
			int rows = (int)math.ceil((boundsMax.y - tree.Origin.y) / maxCellSize);
			for (int iz = 0; iz < rows; iz++)
				for (int ix = 0; ix < cols; ix++)
					tree.Subdivide(0, ix, iz);

			tree.Balance();
			return tree;
		}

		private void CollectSegments(float2[] ring)
		{
			for (int i = 0; i < ring.Length; i++)
			{
				float2 a = ring[i];
				float2 b = ring[(i + 1) % ring.Length];
				float2 lo = math.min(a, b);
				float2 hi = math.max(a, b);
				_segments.Add((a, b, lo, hi));
			}
		}

		private void Subdivide(int depth, int ix, int iz)
		{
			float cs = CellSize(depth);
			float2 min = CellMin(depth, ix, iz);
			float2 max = min + cs;

			var cls = Classify(min, max);
			if (cls == CellClass.Outside)
				return;

			bool canSplit = cs > MinCellSize && depth < MaxDepth;
			bool split;
			if (cls == CellClass.Boundary)
				split = canSplit;
			else
				split = canSplit && _sampleHeight != null && HeightError(min, max) > _maxHeightError;

			if (split)
			{
				Subdivide(depth + 1, ix * 2, iz * 2);
				Subdivide(depth + 1, ix * 2 + 1, iz * 2);
				Subdivide(depth + 1, ix * 2, iz * 2 + 1);
				Subdivide(depth + 1, ix * 2 + 1, iz * 2 + 1);
				return;
			}

			Leaves[(depth, ix, iz)] = new QuadLeaf
			{
				Depth = depth,
				Ix = ix,
				Iz = iz,
				Boundary = cls == CellClass.Boundary
			};
		}

		private CellClass Classify(float2 min, float2 max)
		{
			for (int i = 0; i < _segments.Count; i++)
			{
				var s = _segments[i];
				if (s.Max.x < min.x || s.Min.x > max.x || s.Max.y < min.y || s.Min.y > max.y)
					continue;
				if (SegmentIntersectsRect(s.A, s.B, min, max))
					return CellClass.Boundary;
			}

			float2 center = (min + max) * 0.5f;
			return RegionContains(center) ? CellClass.Inside : CellClass.Outside;
		}

		private bool RegionContains(float2 p)
		{
			for (int i = 0; i < _merged.Count; i++)
				if (_merged[i].Contains(p))
					return true;
			return false;
		}

		private float HeightError(float2 min, float2 max)
		{
			float h00 = _sampleHeight(new float2(min.x, min.y));
			float h10 = _sampleHeight(new float2(max.x, min.y));
			float h01 = _sampleHeight(new float2(min.x, max.y));
			float h11 = _sampleHeight(new float2(max.x, max.y));

			float err = 0f;
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 0.5f, 0.5f));
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 0.5f, 0f));
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 0.5f, 1f));
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 0f, 0.5f));
			err = math.max(err, TestError(min, max, h00, h10, h01, h11, 1f, 0.5f));
			return err;
		}

		private float TestError(float2 min, float2 max, float h00, float h10, float h01, float h11, float u, float v)
		{
			float2 p = new float2(math.lerp(min.x, max.x, u), math.lerp(min.y, max.y, v));
			float approx = math.lerp(math.lerp(h00, h10, u), math.lerp(h01, h11, u), v);
			return math.abs(_sampleHeight(p) - approx);
		}

		public bool TryFindLeaf(float2 p, out QuadLeaf leaf)
		{
			for (int depth = 0; depth <= MaxDepth; depth++)
			{
				float cs = CellSize(depth);
				int ix = (int)math.floor((p.x - Origin.x) / cs);
				int iz = (int)math.floor((p.y - Origin.y) / cs);
				if (Leaves.TryGetValue((depth, ix, iz), out leaf))
					return true;
			}

			leaf = default;
			return false;
		}

		private void Balance()
		{
			var stack = new Stack<(int, int, int)>(Leaves.Keys);
			while (stack.Count > 0)
			{
				var key = stack.Pop();
				if (!Leaves.TryGetValue(key, out var leaf))
					continue;

				float cs = CellSize(leaf.Depth);
				float2 min = CellMin(leaf.Depth, leaf.Ix, leaf.Iz);
				float eps = MinCellSize * 0.25f;
				float2 c = min + cs * 0.5f;

				Span<float2> probes = stackalloc float2[4];
				probes[0] = new float2(c.x, min.y - eps);
				probes[1] = new float2(c.x, min.y + cs + eps);
				probes[2] = new float2(min.x - eps, c.y);
				probes[3] = new float2(min.x + cs + eps, c.y);

				for (int i = 0; i < 4; i++)
				{
					if (!TryFindLeaf(probes[i], out var n))
						continue;
					if (n.Depth >= leaf.Depth - 1)
						continue;

					Leaves.Remove((n.Depth, n.Ix, n.Iz));
					Subdivide(n.Depth + 1, n.Ix * 2, n.Iz * 2);
					Subdivide(n.Depth + 1, n.Ix * 2 + 1, n.Iz * 2);
					Subdivide(n.Depth + 1, n.Ix * 2, n.Iz * 2 + 1);
					Subdivide(n.Depth + 1, n.Ix * 2 + 1, n.Iz * 2 + 1);

					stack.Push((n.Depth + 1, n.Ix * 2, n.Iz * 2));
					stack.Push((n.Depth + 1, n.Ix * 2 + 1, n.Iz * 2));
					stack.Push((n.Depth + 1, n.Ix * 2, n.Iz * 2 + 1));
					stack.Push((n.Depth + 1, n.Ix * 2 + 1, n.Iz * 2 + 1));
					stack.Push(key);
				}
			}
		}

		public bool HasFinerNeighbor(QuadLeaf leaf, float2 probe)
		{
			if (!TryFindLeaf(probe, out var n))
				return false;
			return n.Depth > leaf.Depth;
		}

		private static bool SegmentIntersectsRect(float2 a, float2 b, float2 min, float2 max)
		{
			if (a.x >= min.x && a.x <= max.x && a.y >= min.y && a.y <= max.y)
				return true;
			if (b.x >= min.x && b.x <= max.x && b.y >= min.y && b.y <= max.y)
				return true;

			float2 c00 = new float2(min.x, min.y);
			float2 c10 = new float2(max.x, min.y);
			float2 c11 = new float2(max.x, max.y);
			float2 c01 = new float2(min.x, max.y);

			if (SegmentsIntersect(a, b, c00, c10))
				return true;
			if (SegmentsIntersect(a, b, c10, c11))
				return true;
			if (SegmentsIntersect(a, b, c11, c01))
				return true;
			if (SegmentsIntersect(a, b, c01, c00))
				return true;

			return false;
		}

		private static bool SegmentsIntersect(float2 p1, float2 p2, float2 p3, float2 p4)
		{
			float d1 = Cross(p3, p4, p1);
			float d2 = Cross(p3, p4, p2);
			float d3 = Cross(p1, p2, p3);
			float d4 = Cross(p1, p2, p4);

			if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
				return true;

			return false;
		}

		private static float Cross(float2 a, float2 b, float2 c)
		{
			return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
		}
	}
}
