using System;
using System.Collections.Generic;
using Clipper2ZLib;
using PCG.Attributes;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public static class PolygonClipper
	{
		public const double Scale = 1000.0;

		public static List<Polygon2D> Union(IList<Polygon2D> a, IList<Polygon2D> b)
		{
			var subject = ToPaths(a);
			var clip = ToPaths(b);
			var solution = Clipper.Union(subject, clip, FillRule.NonZero);
			return ToPolygons(solution);
		}

		public static List<Polygon2D> Intersection(IList<Polygon2D> subject, IList<Polygon2D> clip)
		{
			var solution = Clipper.Intersect(ToPaths(subject), ToPaths(clip), FillRule.NonZero);
			return ToPolygons(solution);
		}

		public static List<Polygon2D> Difference(IList<Polygon2D> subject, IList<Polygon2D> clip)
		{
			var solution = Clipper.Difference(ToPaths(subject), ToPaths(clip), FillRule.NonZero);
			return ToPolygons(solution);
		}

		public static List<Polygon2D> Inflate(IList<Polygon2D> input, float delta)
		{
			var paths = ToPaths(input);
			var solution = Clipper.InflatePaths(paths, delta * Scale, JoinType.Miter, EndType.Polygon);
			return ToPolygons(solution);
		}

		public static void SplitByLine(Polygon2D region, float2 a, float2 b, List<Polygon2D> left, List<Polygon2D> right, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			var dir = math.normalize(b - a);
			var normal = new float2(-dir.y, dir.x);

			var subject = new List<Polygon2D> { region };
			var leftRect = HalfPlaneRect(region, a, normal);
			var rightRect = HalfPlaneRect(region, a, -normal);

			left.AddRange(PolygonEdgeClip.Intersection(subject, new List<Polygon2D> { leftRect }, newEdgeWriter));
			right.AddRange(PolygonEdgeClip.Intersection(subject, new List<Polygon2D> { rightRect }, newEdgeWriter));
		}

		private static Polygon2D HalfPlaneRect(Polygon2D region, float2 origin, float2 normal)
		{
			region.GetBounds(out var min, out var max);
			float size = math.length(max - min) + 1f;
			var tangent = new float2(-normal.y, normal.x);
			var center = origin + normal * size;

			var poly = new Polygon2D();
			poly.Outer = new[]
			{
				origin - tangent * size,
				origin + tangent * size,
				center + tangent * size,
				center - tangent * size
			};

			return NormalizeWinding(poly);
		}

		private static Paths64 ToPaths(IList<Polygon2D> polygons)
		{
			var paths = new Paths64();
			for (int i = 0; i < polygons.Count; i++)
			{
				var polygon = polygons[i];
				paths.Add(ToPath(polygon.Outer));
				for (int h = 0; h < polygon.Holes.Count; h++)
				{
					paths.Add(ToPath(polygon.Holes[h]));
				}
			}

			return paths;
		}

		private static Path64 ToPath(float2[] ring)
		{
			var path = new Path64(ring.Length);
			for (int i = 0; i < ring.Length; i++)
			{
				path.Add(new Point64((long)(ring[i].x * Scale), (long)(ring[i].y * Scale)));
			}

			return path;
		}

		private static List<Polygon2D> ToPolygons(Paths64 paths)
		{
			var tree = new PolyTree64();
			var open = new Paths64();
			var clipper = new Clipper64();
			clipper.AddSubject(paths);
			clipper.Execute(ClipType.Union, FillRule.NonZero, tree, open);
			return FromPolyTree(tree);
		}

		private static List<Polygon2D> FromPolyTree(PolyTree64 tree)
		{
			var result = new List<Polygon2D>();
			for (int i = 0; i < tree.Count; i++)
			{
				var outerNode = tree[i];
				var polygon = new Polygon2D();
				polygon.Outer = FromPath(outerNode.Polygon);
				for (int h = 0; h < outerNode.Count; h++)
				{
					polygon.Holes.Add(FromPath(outerNode[h].Polygon));
				}

				result.Add(NormalizeWinding(polygon));
			}

			return result;
		}

		private static float2[] FromPath(Path64 path)
		{
			var ring = new float2[path.Count];
			for (int i = 0; i < path.Count; i++)
			{
				ring[i] = new float2((float)(path[i].X / Scale), (float)(path[i].Y / Scale));
			}

			return ring;
		}

		internal static Polygon2D NormalizeWinding(Polygon2D polygon)
		{
			if (SignedArea(polygon.Outer) < 0f)
				System.Array.Reverse(polygon.Outer);

			for (int h = 0; h < polygon.Holes.Count; h++)
			{
				if (SignedArea(polygon.Holes[h]) > 0f)
					System.Array.Reverse(polygon.Holes[h]);
			}

			return polygon;
		}

		private static float SignedArea(float2[] ring)
		{
			float area = 0f;
			int j = ring.Length - 1;
			for (int i = 0; i < ring.Length; i++)
			{
				area += (ring[j].x + ring[i].x) * (ring[j].y - ring[i].y);
				j = i;
			}

			return area * 0.5f;
		}
	}
}
