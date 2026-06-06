using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Delone;
using PCG.Utilities;
using UnityEngine;

namespace PCG.Mazes.Graphs
{
    /// <summary>
    /// Represents a node in a graph with a 2D position and connected edges.
    /// </summary>
    [Serializable]
    public class GraphNode
    {
        /// <summary>
        /// 2D position of the node.
        /// </summary>
        public Vector2 Point { get; }
        /// <summary>
        /// List of edges connected to this node.
        /// </summary>
        public List<GraphEdge> Edges { get; }

        public GraphNode(Vector2 point)
        {
            Point = point;
            Edges = new List<GraphEdge>();
        }
    }

    /// <summary>
    /// Represents an edge between two nodes in a graph with an associated weight.
    /// </summary>
    [Serializable]
    public class GraphEdge
    {
        /// <summary>
        /// First node of the edge.
        /// </summary>
        public GraphNode Node1 { get; }
        /// <summary>
        /// Second node of the edge.
        /// </summary>
        public GraphNode Node2 { get; }
        /// <summary>
        /// Weight/cost of the edge (used in algorithms like MST).
        /// </summary>
        public float Weight { get; set; }

        public GraphEdge(GraphNode node1, GraphNode node2, float weight)
        {
            Node1 = node1;
            Node2 = node2;
            Weight = weight;
        }
    }

    /// <summary>
    /// Represents a 2D graph structure with nodes and edges.
    /// </summary>
    [Serializable]
    public class Graph
    {
        /// <summary>
        /// All nodes in the graph.
        /// </summary>
        public List<GraphNode> Nodes { get; }
        /// <summary>
        /// All edges in the graph.
        /// </summary>
        public List<GraphEdge> Edges { get; }

        public Graph()
        {
            Nodes = new List<GraphNode>();
            Edges = new List<GraphEdge>();
        }
        
        public void Clear()
        {
            Nodes.Clear();
            Edges.Clear();
        }

        public GraphNode FindNode(Vector2 point)
        {
            return Nodes.Find(node => node.Point.Equals(point));
        }

        public GraphEdge FindEdge(GraphNode node1, GraphNode node2)
        {
            return Edges.Find(edge => 
                (edge.Node1 == node1 && edge.Node2 == node2) || 
                (edge.Node1 == node2 && edge.Node2 == node1));
        }
    }

    public static class GraphBuilder
    {
        public static async UniTask BuildGraph(OperationScope scope, Graph graph, List<Triangle> triangles, CancellationToken ct = default)
        {
            foreach (var triangle in triangles)
            {
                foreach (var point in triangle.Points)
                {
                    if (graph.FindNode(point) == null)
                    {
                        graph.Nodes.Add(new GraphNode(point));
                    }
                    
                    await scope.Step(ct: ct);
                }
            }

            foreach (var triangle in triangles)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector2 p1 = triangle.Points[i];
                    Vector2 p2 = triangle.Points[(i + 1) % 3];

                    var node1 = graph.FindNode(p1);
                    var node2 = graph.FindNode(p2);

                    if (graph.FindEdge(node1, node2) == null)
                    {
                        var edge = new GraphEdge(node1, node2, 0.5f);
                        graph.Edges.Add(edge);
                        node1.Edges.Add(edge);
                        node2.Edges.Add(edge);
                    }
                    
                    await scope.Step(ct: ct);
                }
            }
        }

        public static async UniTask BuildGrid(OperationScope scope, Graph graph, int width, int height, float cellWidth, float cellHeight, CancellationToken ct = default)
        {
            GraphNode[][] gridNodes = new GraphNode[width][];
            for (int index = 0; index < width; index++)
            {
                gridNodes[index] = new GraphNode[height];
            }

            var halfX = width * cellWidth * 0.5f;
            var halfY = height * cellHeight * 0.5f;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var point = new Vector2(x * cellWidth - halfX, y * cellHeight - halfY);
                    var node = graph.FindNode(point);
                    if (node == null)
                    {
                        node = new GraphNode(point);
                        graph.Nodes.Add(node);
                    }
                    gridNodes[x][y] = node;

                    await scope.Step(ct: ct);
                }
            }

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var node = gridNodes[x][y];

                    if (x < width - 1)
                    {
                        var rightNode = gridNodes[x + 1][y];
                        if (graph.FindEdge(node, rightNode) == null)
                        {
                            var edge = new GraphEdge(node, rightNode, 0.5f);
                            graph.Edges.Add(edge);
                            node.Edges.Add(edge);
                            rightNode.Edges.Add(edge);
                        }
                        
                        await scope.Step(ct: ct);
                    }

                    if (y < height - 1)
                    {
                        var topNode = gridNodes[x][y + 1];
                        if (graph.FindEdge(node, topNode) == null)
                        {
                            var edge = new GraphEdge(node, topNode, 0.5f);
                            graph.Edges.Add(edge);
                            node.Edges.Add(edge);
                            topNode.Edges.Add(edge);
                        }
                        
                        await scope.Step(ct: ct);
                    }
                }
            }
        }
    }
}