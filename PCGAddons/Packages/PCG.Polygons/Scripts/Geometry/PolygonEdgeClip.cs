using System;
using System.Collections.Generic;
using Clipper2ZLib;
using PCG.Attributes;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public static class PolygonEdgeClip
	{
		private struct EdgeSource
		{
			public Polygon2D Polygon;
			public int LocalEdge;
			public float2 A;
			public float2 B;
		}

		public static List<Polygon2D> Difference(IList<Polygon2D> subject, IList<Polygon2D> clip, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			return Execute(ClipType.Difference, subject, clip, newEdgeWriter);
		}

		public static List<Polygon2D> Intersection(IList<Polygon2D> subject, IList<Polygon2D> clip, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			return Execute(ClipType.Intersection, subject, clip, newEdgeWriter);
		}

		public static List<Polygon2D> Union(IList<Polygon2D> subject, IList<Polygon2D> clip, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			return Execute(ClipType.Union, subject, clip, newEdgeWriter);
		}

		public static Polygon2D BuildStrip(float2 a, float2 b, float width)
		{
			var dir = b - a;
			float len = math.length(dir);
			if (len < 1e-4f)
				return null;

			dir /= len;
			var offset = new float2(-dir.y, dir.x) * (width * 0.5f);
			var polygon = new Polygon2D();
			polygon.Outer = new[] { a + offset, b + offset, b - offset, a - offset };
			return polygon;
		}

		private static List<Polygon2D> Execute(ClipType clipType, IList<Polygon2D> subject, IList<Polygon2D> clip, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			var table = new List<EdgeSource>();
			var subjectPaths = BuildSubjectPaths(subject, table);
			var clipPaths = BuildClipPaths(clip);

			var clipper = new Clipper64();
			clipper.AddSubject(subjectPaths);
			clipper.AddClip(clipPaths);

			var tree = new PolyTree64();
			var open = new Paths64();
			clipper.Execute(clipType, FillRule.NonZero, tree, open);

			var result = new List<Polygon2D>();
			BuildPolygons(tree, table, newEdgeWriter, result);
			return result;
		}

		private static Paths64 BuildSubjectPaths(IList<Polygon2D> subject, List<EdgeSource> table)
		{
			var paths = new Paths64();
			for (int p = 0; p < subject.Count; p++)
			{
				var polygon = subject[p];
				AppendRing(paths, table, polygon, polygon.Outer, 0);
				int offset = polygon.Outer.Length;
				for (int h = 0; h < polygon.Holes.Count; h++)
				{
					AppendRing(paths, table, polygon, polygon.Holes[h], offset);
					offset += polygon.Holes[h].Length;
				}
			}

			return paths;
		}

		private static void AppendRing(Paths64 paths, List<EdgeSource> table, Polygon2D polygon, float2[] ring, int localOffset)
		{
			var path = new Path64(ring.Length);
			for (int i = 0; i < ring.Length; i++)
			{
				int next = (i + 1) % ring.Length;
				table.Add(new EdgeSource
				{
					Polygon = polygon,
					LocalEdge = localOffset + i,
					A = ring[i],
					B = ring[next]
				});

				var point = new Point64((long)(ring[i].x * PolygonClipper.Scale), (long)(ring[i].y * PolygonClipper.Scale));
				path.Add(point);
			}

			paths.Add(path);
		}

		private static Paths64 BuildClipPaths(IList<Polygon2D> clip)
		{
			var paths = new Paths64();
			for (int p = 0; p < clip.Count; p++)
			{
				var polygon = clip[p];
				paths.Add(ClipRing(polygon.Outer));
				for (int h = 0; h < polygon.Holes.Count; h++)
				{
					paths.Add(ClipRing(polygon.Holes[h]));
				}
			}

			return paths;
		}

		private static Path64 ClipRing(float2[] ring)
		{
			var path = new Path64(ring.Length);
			for (int i = 0; i < ring.Length; i++)
			{
				var point = new Point64((long)(ring[i].x * PolygonClipper.Scale), (long)(ring[i].y * PolygonClipper.Scale));
				path.Add(point);
			}

			return path;
		}

		private static void BuildPolygons(PolyTree64 tree, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter, List<Polygon2D> result)
		{
			for (int i = 0; i < tree.Count; i++)
			{
				var node = tree[i];
				var polygon = new Polygon2D();
				polygon.Outer = ToRing(node.Polygon);
				for (int h = 0; h < node.Count; h++)
				{
					polygon.Holes.Add(ToRing(node[h].Polygon));
				}

				PolygonClipper.NormalizeWinding(polygon);
				AssignEdges(polygon, table, newEdgeWriter);
				result.Add(polygon);
			}
		}

		private static float2[] ToRing(Path64 path)
		{
			int n = path.Count;
			var ring = new float2[n];
			for (int i = 0; i < n; i++)
			{
				ring[i] = new float2((float)(path[i].X / PolygonClipper.Scale), (float)(path[i].Y / PolygonClipper.Scale));
			}

			return ring;
		}

		private static void AssignEdges(Polygon2D polygon, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			AssignRing(polygon.Outer, table, newEdgeWriter, polygon.EdgeAttributes);
			for (int h = 0; h < polygon.Holes.Count; h++)
			{
				AssignRing(polygon.Holes[h], table, newEdgeWriter, polygon.EdgeAttributes);
			}
		}

		private static void AssignRing(float2[] ring, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter, PcgAttributeSet edgeAttributes)
		{
			int n = ring.Length;
			for (int i = 0; i < n; i++)
			{
				var a = ring[i];
				var b = ring[(i + 1) % n];
				int sourceId = GeometricSource(a, b, table);
				if (sourceId > 0)
				{
					var src = table[sourceId - 1];
					if (src.Polygon.HasEdgeData())
						edgeAttributes.AppendRow(src.Polygon.EdgeAttributes, src.LocalEdge);
					else
						edgeAttributes.AddRow();

					continue;
				}

				int row = edgeAttributes.AddRow();
				newEdgeWriter?.Invoke(edgeAttributes, row);
			}
		}

		private static int GeometricSource(float2 a, float2 b, List<EdgeSource> table)
		{
			var m = (a + b) * 0.5f;
			for (int i = 0; i < table.Count; i++)
			{
				if (OnSegment(m, table[i].A, table[i].B))
					return i + 1;
			}

			return 0;
		}

		private static bool OnSegment(float2 m, float2 c, float2 d)
		{
			var dir = d - c;
			float len = math.length(dir);
			if (len < 1e-4f)
				return false;

			dir /= len;
			float cross = dir.x * (m.y - c.y) - dir.y * (m.x - c.x);
			if (math.abs(cross) > 0.01f)
				return false;

			float t = math.dot(m - c, dir);
			return t >= -0.01f && t <= len + 0.01f;
		}
	}
}
