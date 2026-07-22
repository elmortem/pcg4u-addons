using System;
using System.Collections.Generic;
using Clipper2ZLib;
using PCG.Polygons;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal static class SweepRibbonPolygonUnion
	{
		internal const double Scale = 100000.0;
		private const float CoordinateLimit = 10000000f;

		internal static bool TryUnion(IReadOnlyList<SweepRibbonSourceTriangle> sources, out List<Polygon2D> polygons, out string failure)
		{
			polygons = new List<Polygon2D>();
			failure = null;
			if (sources == null || sources.Count == 0)
			{
				failure = "GlobalSourcesEmpty";
				return false;
			}

			var paths = new Paths64(sources.Count);
			var exact = new Dictionary<(long, long), float2>();
			for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
			{
				SweepRibbonSourceTriangle source = sources[sourceIndex];
				if (!TryPoint(source.A, exact, out Point64 a) || !TryPoint(source.B, exact, out Point64 b) || !TryPoint(source.C, exact, out Point64 c))
				{
					failure = "GlobalCoordinateInvalid-" + source.SourceOrder;
					return false;
				}

				var path = new Path64(3) { a, b, c };
				if (Clipper.Area(path) < 0.0)
				{
					Point64 value = path[1];
					path[1] = path[2];
					path[2] = value;
				}
				if (math.abs((float)Clipper.Area(path)) >= 1.0f)
					paths.Add(path);
			}

			if (paths.Count == 0)
			{
				failure = "GlobalPathsEmpty";
				return false;
			}

			try
			{
				var tree = new PolyTree64();
				var clipper = new Clipper64();
				clipper.AddSubject(paths);
				if (!clipper.Execute(ClipType.Union, FillRule.NonZero, tree))
				{
					failure = "GlobalUnionFailed";
					return false;
				}
				for (int child = 0; child < tree.Count; child++)
					AppendNode(tree[child], exact, polygons);
			}
			catch (Exception exception)
			{
				failure = "GlobalUnionException-" + exception.GetType().Name;
				return false;
			}

			for (int polygon = polygons.Count - 1; polygon >= 0; polygon--)
			{
				Polygon2D value = polygons[polygon];
				if (value.Outer == null || value.Outer.Length < 3 || math.abs(SignedArea(value.Outer)) < 1e-10f)
					polygons.RemoveAt(polygon);
			}
			polygons.Sort(ComparePolygons);
			if (polygons.Count == 0)
			{
				failure = "GlobalUnionEmpty";
				return false;
			}
			return true;
		}

		private static bool TryPoint(float2 value, Dictionary<(long, long), float2> exact, out Point64 point)
		{
			point = default;
			if (!math.all(math.isfinite(value)) || math.any(math.abs(value) > CoordinateLimit))
				return false;
			long x = (long)math.round((double)value.x * Scale);
			long y = (long)math.round((double)value.y * Scale);
			var key = (x, y);
			if (!exact.ContainsKey(key))
				exact.Add(key, value);
			point = new Point64(x, y);
			return true;
		}

		private static void AppendNode(PolyPath64 node, Dictionary<(long, long), float2> exact, List<Polygon2D> polygons)
		{
			if (!node.IsHole && node.Polygon != null)
			{
				float2[] outer = Ring(node.Polygon, exact);
				if (outer.Length >= 3)
				{
					if (SignedArea(outer) < 0f)
						Array.Reverse(outer);
					var polygon = new Polygon2D { Outer = outer };
					for (int child = 0; child < node.Count; child++)
					{
						PolyPath64 holeNode = node[child];
						if (!holeNode.IsHole || holeNode.Polygon == null)
							continue;
						float2[] hole = Ring(holeNode.Polygon, exact);
						if (hole.Length < 3 || math.abs(SignedArea(hole)) < 1e-10f)
							continue;
						if (SignedArea(hole) > 0f)
							Array.Reverse(hole);
						polygon.Holes.Add(hole);
					}
					polygon.Holes.Sort(CompareRings);
					polygons.Add(polygon);
				}
			}

			for (int child = 0; child < node.Count; child++)
				AppendNode(node[child], exact, polygons);
		}

		private static float2[] Ring(Path64 path, Dictionary<(long, long), float2> exact)
		{
			var result = new List<float2>(path.Count);
			for (int pointIndex = 0; pointIndex < path.Count; pointIndex++)
			{
				Point64 point = path[pointIndex];
				var key = (point.X, point.Y);
				float2 value = exact.TryGetValue(key, out float2 source)
					? source
					: new float2((float)(point.X / Scale), (float)(point.Y / Scale));
				if (result.Count == 0 || math.distancesq(result[result.Count - 1], value) > 1e-14f)
					result.Add(value);
			}
			if (result.Count > 1 && math.distancesq(result[0], result[result.Count - 1]) <= 1e-14f)
				result.RemoveAt(result.Count - 1);
			return result.ToArray();
		}

		private static int ComparePolygons(Polygon2D first, Polygon2D second)
		{
			Bounds(first.Outer, out float2 firstMin, out _);
			Bounds(second.Outer, out float2 secondMin, out _);
			int order = firstMin.x.CompareTo(secondMin.x);
			if (order != 0)
				return order;
			order = firstMin.y.CompareTo(secondMin.y);
			if (order != 0)
				return order;
			order = -math.abs(SignedArea(first.Outer)).CompareTo(math.abs(SignedArea(second.Outer)));
			return order != 0 ? order : first.Outer.Length.CompareTo(second.Outer.Length);
		}

		private static int CompareRings(float2[] first, float2[] second)
		{
			Bounds(first, out float2 firstMin, out _);
			Bounds(second, out float2 secondMin, out _);
			int order = firstMin.x.CompareTo(secondMin.x);
			if (order != 0)
				return order;
			order = firstMin.y.CompareTo(secondMin.y);
			return order != 0 ? order : first.Length.CompareTo(second.Length);
		}

		private static void Bounds(float2[] ring, out float2 minimum, out float2 maximum)
		{
			minimum = new float2(float.MaxValue);
			maximum = new float2(float.MinValue);
			for (int point = 0; point < ring.Length; point++)
			{
				minimum = math.min(minimum, ring[point]);
				maximum = math.max(maximum, ring[point]);
			}
		}

		private static float SignedArea(IReadOnlyList<float2> ring)
		{
			float area = 0f;
			for (int point = 0; point < ring.Count; point++)
			{
				float2 a = ring[point];
				float2 b = ring[(point + 1) % ring.Count];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}
	}
}
