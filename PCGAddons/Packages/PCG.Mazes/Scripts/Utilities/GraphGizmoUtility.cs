using PCG.Mazes.Graphs;
using UnityEngine;
using PCG.Options;

namespace PCG.Mazes.Utilities
{
	public static class GraphGizmoUtility
	{
		public static void DrawGraph(Graph graph)
		{
			if (graph == null || graph.Edges == null || graph.Edges.Count <= 0)
				return;

			foreach (var edge in graph.Edges)
			{
				Vector3 start = new Vector3(edge.Node1.Point.x, 0f, edge.Node1.Point.y);
				Vector3 end = new Vector3(edge.Node2.Point.x, 0f, edge.Node2.Point.y);
				Gizmos.DrawLine(start, end);
			}
		}
		
		public static void DrawGraph(Graph graph, GizmosOptions gizmosOptions, Transform transform)
		{
			if (graph == null || graph.Edges == null || graph.Edges.Count <= 0)
				return;
			
			Gizmos.color = gizmosOptions.Color;
			Gizmos.matrix = transform.localToWorldMatrix;
			
			DrawGraph(graph);
			
			Gizmos.matrix = Matrix4x4.identity;
		}
		
		
	}
}