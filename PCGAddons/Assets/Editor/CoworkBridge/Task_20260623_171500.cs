using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class Task_20260623_171500
{
	public static string Run()
	{
		var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PCG.Polygons");
		if (asm == null)
			return "FAIL: assembly PCG.Polygons not loaded";

		var builderType = asm.GetType("PCG.Polygons.RegionMeshBuilder");
		var quadtreeType = asm.GetType("PCG.Polygons.MeshQuadtree");
		var leafType = asm.GetType("PCG.Polygons.QuadLeaf");
		if (builderType == null || quadtreeType == null || leafType == null)
			return "FAIL: new types missing (RegionMeshBuilder/MeshQuadtree/QuadLeaf)";

		var build = builderType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
		if (build == null || build.GetParameters().Length != 9)
			return "FAIL: RegionMeshBuilder.Build signature mismatch";

		var qtBuild = quadtreeType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
		if (qtBuild == null || qtBuild.GetParameters().Length != 8)
			return "FAIL: MeshQuadtree.Build signature mismatch";

		var mathAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Unity.Mathematics");
		var float2Type = mathAsm.GetType("Unity.Mathematics.float2");
		var float2Ctor = float2Type.GetConstructor(new[] { typeof(float), typeof(float) });

		Func<float, float, object> mk = (x, y) => float2Ctor.Invoke(new object[] { x, y });
		var outer = Array.CreateInstance(float2Type, 4);
		outer.SetValue(mk(0f, 0f), 0);
		outer.SetValue(mk(10f, 0f), 1);
		outer.SetValue(mk(10f, 10f), 2);
		outer.SetValue(mk(0f, 10f), 3);

		var polyType = asm.GetType("PCG.Polygons.Polygon2D");
		var poly = Activator.CreateInstance(polyType);
		polyType.GetField("Outer").SetValue(poly, outer);

		var regionType = asm.GetType("PCG.Polygons.RegionSet");
		var region = Activator.CreateInstance(regionType);
		regionType.GetMethod("AddRegion").Invoke(region, new[] { poly });

		var args = new object[]
		{
			region,
			null,
			Vector3.zero,
			0.25f,
			1f,
			16f,
			6,
			0.1f,
			0.1f
		};

		var data = build.Invoke(null, args);
		var dataType = data.GetType();
		var verts = (Array)dataType.GetField("Vertices").GetValue(data);
		var tris = (Array)dataType.GetField("Triangles").GetValue(data);
		int vCount = verts == null ? 0 : verts.Length;
		int tCount = tris == null ? 0 : tris.Length / 3;

		if (vCount < 4 || tCount < 2)
			return $"FAIL: degenerate mesh verts={vCount} triangles={tCount}";

		Debug.Log($"[Smoke] no-terrain square 10x10: verts={vCount}, triangles={tCount}");
		return $"OK: compiled and ran. Build returned verts={vCount}, triangles={tCount}";
	}
}
