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
			clipper.ZCallback = OnZ;
			clipper.AddSubject(subjectPaths);
			clipper.AddClip(clipPaths);

			var tree = new PolyTree64();
			var open = new Paths64();
			clipper.Execute(clipType, FillRule.NonZero, tree, open);

			var result = new List<Polygon2D>();
			BuildPolygons(tree, table, newEdgeWriter, result);
			return result;
		}

		private static void OnZ(Point64 e1bot, Point64 e1top, Point64 e2bot, Point64 e2top, ref Point64 ip)
		{
			long z = e1bot.Z;
			if (e1top.Z > z)
				z = e1top.Z;
			if (e2bot.Z > z)
				z = e2bot.Z;
			if (e2top.Z > z)
				z = e2top.Z;

			ip.Z = z;
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
				int id = table.Count + 1;
				table.Add(new EdgeSource
				{
					Polygon = polygon,
					LocalEdge = localOffset + i,
					A = ring[i],
					B = ring[next]
				});

				var point = new Point64((long)(ring[i].x * PolygonClipper.Scale), (long)(ring[i].y * PolygonClipper.Scale));
				point.Z = id;
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
				point.Z = 0;
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
				polygon.Outer = ResolveRing(node.Polygon, table, newEdgeWriter, polygon.EdgeAttributes);
				for (int h = 0; h < node.Count; h++)
				{
					polygon.Holes.Add(ResolveRing(node[h].Polygon, table, newEdgeWriter, polygon.EdgeAttributes));
				}

				PolygonClipper.NormalizeWinding(polygon);
				result.Add(polygon);
			}
		}

		private static float2[] ResolveRing(Path64 path, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter, PcgAttributeSet edgeAttributes)
		{
			int n = path.Count;
			var ring = new float2[n];
			for (int i = 0; i < n; i++)
			{
				ring[i] = new float2((float)(path[i].X / PolygonClipper.Scale), (float)(path[i].Y / PolygonClipper.Scale));
			}

			for (int i = 0; i < n; i++)
			{
				int next = (i + 1) % n;
				int sourceId = ClassifyEdge(ring[i], ring[next], path[i].Z, path[next].Z, table);
				if (sourceId > 0)
				{
					var src = table[sourceId - 1];
					if (src.Polygon.HasEdgeData())
					{
						edgeAttributes.AppendRow(src.Polygon.EdgeAttributes, src.LocalEdge);
						continue;
					}
				}

				int row = edgeAttributes.AddRow();
				newEdgeWriter?.Invoke(edgeAttributes, row);
			}

			return ring;
		}

		private static int ClassifyEdge(float2 a, float2 b, long za, long zb, List<EdgeSource> table)
		{
			int candidate = TryCandidate(a, b, za, table);
			if (candidate > 0)
				return candidate;

			return TryCandidate(a, b, zb, table);
		}

		private static int TryCandidate(float2 a, float2 b, long id, List<EdgeSource> table)
		{
			if (id <= 0 || id > table.Count)
				return 0;

			var src = table[(int)id - 1];
			if (IsCollinearOverlap(a, b, src.A, src.B))
				return (int)id;

			return 0;
		}

		private static bool IsCollinearOverlap(float2 a, float2 b, float2 c, float2 d)
		{
			const float eps = 0.001f;
			var dir = d - c;
			float len = math.length(dir);
			if (len < eps)
				return false;

			dir /= len;
			float distA = math.abs(Cross(dir, a - c));
			float distB = math.abs(Cross(dir, b - c));
			return distA < eps && distB < eps;
		}

		private static float Cross(float2 u, float2 v)
		{
			return u.x * v.y - u.y * v.x;
		}
	}
}
