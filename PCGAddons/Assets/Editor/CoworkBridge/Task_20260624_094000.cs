using System;
using System.Linq;
using System.Reflection;

public static class Task_20260624_094000
{
	public static string Run()
	{
		var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PCG.Polygons");
		if (asm == null)
			return "FAIL: assembly PCG.Polygons not loaded";

		var node = asm.GetType("PCG.Polygons.City.RegionToMeshNode");
		if (node == null)
			return "FAIL: RegionToMeshNode type missing";

		var edAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PCG.Polygons.Editor");
		var exec = edAsm?.GetType("PCG.Polygons.City.RegionToMeshNodeExecutor");
		if (exec == null)
			return "FAIL: RegionToMeshNodeExecutor type missing";

		return "OK: project compiled, RegionToMeshNode + RegionToMeshNodeExecutor present";
	}
}
