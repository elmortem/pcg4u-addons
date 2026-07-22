using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using PCG.Sweep;
using UnityEngine;

public static class Task_20260722_232000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		var top = new SweepMeshData
		{
			Vertices = new[]
			{
				new Vector3(-2f, 0f, -2f),
				new Vector3(-2f, 0f, 2f),
				new Vector3(2f, 0f, -2f),
				new Vector3(2f, 0f, 2f)
			},
			Uvs = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one },
			Triangles = new[] { 0, 1, 2, 2, 1, 3 }
		};

		Type builderType = FindType("PCG.Sweep.SweepPrismBuilder");
		MethodInfo extrude = builderType.GetMethod("Extrude", BindingFlags.Static | BindingFlags.NonPublic);
		var prism = (SweepMeshData)extrude.Invoke(null, new object[] { top, 1f });
		var mesh = new Mesh
		{
			vertices = prism.Vertices,
			uv = prism.Uvs,
			triangles = prism.Triangles
		};
		mesh.RecalculateNormals();
		mesh.RecalculateTangents();

		var vertices = mesh.vertices;
		var triangles = mesh.triangles;
		var normals = mesh.normals;
		var incident = new List<Vector3>[vertices.Length];
		for (int i = 0; i < incident.Length; i++)
			incident[i] = new List<Vector3>();
		for (int i = 0; i + 2 < triangles.Length; i += 3)
		{
			int a = triangles[i];
			int b = triangles[i + 1];
			int c = triangles[i + 2];
			Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
			incident[a].Add(face);
			incident[b].Add(face);
			incident[c].Add(face);
		}

		int horizontalWallShared = 0;
		for (int v = 0; v < incident.Length; v++)
		{
			bool horizontal = false;
			bool wall = false;
			for (int f = 0; f < incident[v].Count; f++)
			{
				horizontal |= Mathf.Abs(incident[v][f].y) > 0.9f;
				wall |= Mathf.Abs(incident[v][f].y) < 0.1f;
			}
			if (horizontal && wall)
				horizontalWallShared++;
		}

		float topMinY = 1f;
		float bottomMaxY = -1f;
		float wallMaxAbsY = 0f;
		for (int i = 0; i < 4; i++)
			topMinY = Mathf.Min(topMinY, normals[i].y);
		for (int i = 4; i < 8; i++)
			bottomMaxY = Mathf.Max(bottomMaxY, normals[i].y);
		for (int i = 8; i < normals.Length; i++)
			wallMaxAbsY = Mathf.Max(wallMaxAbsY, Mathf.Abs(normals[i].y));

		bool pass = vertices.Length == 16 && triangles.Length / 3 == 12 && horizontalWallShared == 0 &&
			topMinY > 0.999f && bottomMaxY < -0.999f && wallMaxAbsY < 0.001f && mesh.tangents.Length == vertices.Length;
		UnityEngine.Object.DestroyImmediate(mesh);
		return "pass=" + pass +
			" v/t=" + vertices.Length + "/" + triangles.Length / 3 +
			" horizontalWallShared=" + horizontalWallShared +
			" topMinY=" + topMinY.ToString("F4") +
			" bottomMaxY=" + bottomMaxY.ToString("F4") +
			" wallMaxAbsY=" + wallMaxAbsY.ToString("F4");
	}

	private static Type FindType(string name)
	{
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type type = assembly.GetType(name, false);
			if (type != null)
				return type;
		}
		throw new InvalidOperationException(name);
	}
}
