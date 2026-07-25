using System.Collections.Generic;
using System.Linq;
using PCG.Polygons.City;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public static class RoadPolylineBuilder
	{
		public static Dictionary<int, List<RoadSegment>> CollectByDepth(RegionSet blocks)
		{
			var seen = new HashSet<int4>();
			var byDepth = new Dictionary<int, List<RoadSegment>>();

			foreach (var block in blocks.Regions)
			{
				if (!block.HasEdgeData() || !block.EdgeAttributes.HasColumn(CityAttributes.Width))
					continue;

				int n = block.Outer.Length;
				for (int e = 0; e < n; e++)
				{
					float w = block.GetEdge<float>(CityAttributes.Width, e);
					if (w <= 0f)
						continue;

					var a = block.Outer[e];
					var b = block.Outer[(e + 1) % n];
					if (!seen.Add(Key(a, b)))
						continue;

					int d = block.GetEdge<int>(CityAttributes.CutDepth, e);
					if (!byDepth.TryGetValue(d, out var list))
					{
						list = new List<RoadSegment>();
						byDepth[d] = list;
					}

					list.Add(new RoadSegment { A = a, B = b, Depth = d, Width = w });
				}
			}

			return byDepth;
		}

		public static void PruneShortDeadEnds(Dictionary<int, List<RoadSegment>> byDepth, float minimumLength)
		{
			if (byDepth == null || minimumLength <= 0f)
				return;

			bool changed;
			do
			{
				changed = false;
				var records = new List<SegmentRecord>();
				foreach (var pair in byDepth)
					for (int i = 0; i < pair.Value.Count; i++)
						records.Add(new SegmentRecord(pair.Key, i, pair.Value[i]));
				if (records.Count == 0)
					return;

				var vertices = new Dictionary<int2, int>();
				var ends = new int2[records.Count];
				var adjacency = new List<List<int>>();
				for (int i = 0; i < records.Count; i++)
				{
					int a = VertexId(records[i].Segment.A, vertices, adjacency);
					int b = VertexId(records[i].Segment.B, vertices, adjacency);
					ends[i] = new int2(a, b);
					adjacency[a].Add(i);
					adjacency[b].Add(i);
				}

				var remove = new HashSet<int>();
				for (int vertex = 0; vertex < adjacency.Count; vertex++)
				{
					if (adjacency[vertex].Count != 1)
						continue;

					var branch = new List<int>();
					float length = 0f;
					int currentVertex = vertex;
					int currentEdge = adjacency[vertex][0];
					var seen = new HashSet<int>();
					while (seen.Add(currentEdge))
					{
						branch.Add(currentEdge);
						RoadSegment segment = records[currentEdge].Segment;
						length += math.distance(segment.A, segment.B);
						if (length >= minimumLength)
							break;

						int nextVertex = ends[currentEdge].x == currentVertex
							? ends[currentEdge].y
							: ends[currentEdge].x;
						if (adjacency[nextVertex].Count != 2)
							break;

						int nextEdge = adjacency[nextVertex][0] == currentEdge
							? adjacency[nextVertex][1]
							: adjacency[nextVertex][0];
						currentVertex = nextVertex;
						currentEdge = nextEdge;
					}

					if (length < minimumLength)
						for (int i = 0; i < branch.Count; i++)
							remove.Add(branch[i]);
				}

				if (remove.Count == 0)
					continue;

				foreach (var group in records
					         .Select((record, index) => (record, index))
					         .Where(item => remove.Contains(item.index))
					         .GroupBy(item => item.record.Depth))
				{
					foreach (int index in group.Select(item => item.record.Index).OrderByDescending(index => index))
						byDepth[group.Key].RemoveAt(index);
				}
				changed = true;
			}
			while (changed);
		}

		public static void Chain(List<RoadSegment> segments, List<float2[]> openPaths, List<float2[]> closedPaths)
		{
			var verts = new List<float2>();
			var vid = new Dictionary<int2, int>();
			var ends = new List<int2>();
			var adj = new List<List<int>>();

			for (int i = 0; i < segments.Count; i++)
			{
				int v0 = Vertex(segments[i].A, verts, vid, adj);
				int v1 = Vertex(segments[i].B, verts, vid, adj);
				ends.Add(new int2(v0, v1));
				adj[v0].Add(i);
				adj[v1].Add(i);
			}

			var used = new bool[segments.Count];

			for (int u = 0; u < verts.Count; u++)
			{
				if (adj[u].Count == 2)
					continue;

				for (int j = 0; j < adj[u].Count; j++)
				{
					int e = adj[u][j];
					if (used[e])
						continue;

					openPaths.Add(Trace(u, e, ends, adj, used, verts, false));
				}
			}

			for (int i = 0; i < segments.Count; i++)
			{
				if (used[i])
					continue;

				int start = ends[i].x;
				closedPaths.Add(Trace(start, i, ends, adj, used, verts, true));
			}
		}

		private static float2[] Trace(int startVertex, int startEdge, List<int2> ends, List<List<int>> adj, bool[] used, List<float2> verts, bool closed)
		{
			var points = new List<float2>();
			points.Add(verts[startVertex]);

			int cur = startVertex;
			int e = startEdge;

			while (true)
			{
				used[e] = true;
				int other = ends[e].x == cur ? ends[e].y : ends[e].x;
				points.Add(verts[other]);

				if (adj[other].Count != 2)
					break;

				int next = -1;
				for (int k = 0; k < adj[other].Count; k++)
				{
					int cand = adj[other][k];
					if (!used[cand])
					{
						next = cand;
						break;
					}
				}

				if (next < 0)
					break;

				cur = other;
				e = next;
			}

			if (closed && points.Count > 1)
				points.RemoveAt(points.Count - 1);

			return points.ToArray();
		}

		private static int Vertex(float2 p, List<float2> verts, Dictionary<int2, int> vid, List<List<int>> adj)
		{
			var key = Quant(p);
			if (vid.TryGetValue(key, out int id))
				return id;

			id = verts.Count;
			verts.Add(p);
			vid[key] = id;
			adj.Add(new List<int>());
			return id;
		}

		private static int VertexId(float2 point, Dictionary<int2, int> vertices, List<List<int>> adjacency)
		{
			int2 key = Quant(point);
			if (vertices.TryGetValue(key, out int id))
				return id;
			id = vertices.Count;
			vertices[key] = id;
			adjacency.Add(new List<int>());
			return id;
		}

		private static int2 Quant(float2 p)
		{
			return new int2((int)math.round(p.x * 1000.0), (int)math.round(p.y * 1000.0));
		}

		private static int4 Key(float2 a, float2 b)
		{
			var qa = Quant(a);
			var qb = Quant(b);
			bool swap = qa.x > qb.x || (qa.x == qb.x && qa.y > qb.y);
			if (swap)
				return new int4(qb.x, qb.y, qa.x, qa.y);

			return new int4(qa.x, qa.y, qb.x, qb.y);
		}

		private readonly struct SegmentRecord
		{
			public readonly int Depth;
			public readonly int Index;
			public readonly RoadSegment Segment;

			public SegmentRecord(int depth, int index, RoadSegment segment)
			{
				Depth = depth;
				Index = index;
				Segment = segment;
			}
		}
	}
}
