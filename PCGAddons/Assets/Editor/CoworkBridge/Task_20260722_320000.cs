using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

public static class Task_20260722_320000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		Type fan = FindType("PCG.Sweep.SweepRibbonCornerFanBuilder");
		Type splitter = FindType("PCG.Sweep.SweepRibbonSplitter");
		MethodInfo fanBuild = fan?.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static);
		MethodInfo classify = splitter?.GetMethod("ClassifyStep", BindingFlags.NonPublic | BindingFlags.Static);
		return "fanType=" + (fan != null) + " fanBuild=" + (fanBuild != null) + " classify=" + (classify != null);
	}

	private static Type FindType(string n) { foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType(n, false); if (t != null) return t; } return null; }
}
