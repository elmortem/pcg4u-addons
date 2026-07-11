using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public static class Task_20260710_120000
{
	private const string LibPrefix = "com.elmortem.pcg.";
	private const int Rev = 3;

	public static string Run()
	{
		var edAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PCG.Editors");
		if (edAsm == null)
			return "FAIL: assembly PCG.Editors not loaded";

		var catalog = edAsm.GetType("PCG.Authoring.PcgNodeCatalog");
		if (catalog == null)
			return "FAIL: PcgNodeCatalog type missing";

		catalog.GetMethod("Refresh").Invoke(null, null);

		var all = (IEnumerable)catalog.GetMethod("GetAll").Invoke(null, null);
		var libs = (IEnumerable)catalog.GetMethod("GetLibraries").Invoke(null, null);
		var diags = (IEnumerable)catalog.GetMethod("GetDiagnostics").Invoke(null, null);

		var sb = new StringBuilder();

		sb.AppendLine("=== LIBRARIES ===");
		int libCount = 0;
		foreach (var lib in libs)
		{
			var id = (string)Prop(lib, "Id");
			if (id == null || !id.StartsWith(LibPrefix))
				continue;
			libCount++;
			sb.AppendLine($"{id} | display='{Prop(lib, "DisplayName")}' | complete={Prop(lib, "MetadataComplete")} | asms=[{string.Join(",", ((IEnumerable)Prop(lib, "AssemblyNames")).Cast<string>())}]");
		}

		sb.AppendLine();
		sb.AppendLine("=== NODES ===");
		int nodeCount = 0;
		int incomplete = 0;
		int noExecutor = 0;
		foreach (var d in all)
		{
			var libId = (string)Prop(d, "LibraryId");
			if (libId == null || !libId.StartsWith(LibPrefix))
				continue;
			nodeCount++;
			bool complete = (bool)Prop(d, "MetadataComplete");
			bool hasExec = (bool)Prop(d, "HasExecutor");
			if (!complete)
				incomplete++;
			if (!hasExec)
				noExecutor++;
			sb.AppendLine($"{Prop(d, "TypeId")} | complete={complete} | executor={hasExec} | cat='{Prop(d, "Category")}' | display='{Prop(d, "DisplayName")}'");
		}

		sb.AppendLine();
		sb.AppendLine("=== DIAGNOSTICS (addons) ===");
		int diagCount = 0;
		foreach (var dg in diags)
		{
			var libId = (string)Prop(dg, "LibraryId");
			var typeId = (string)Prop(dg, "TypeId");
			bool mine = (libId != null && libId.StartsWith(LibPrefix)) ||
				(typeId != null && (typeId.StartsWith("PCG.Splines:") || typeId.StartsWith("PCG.Mazes:") ||
					typeId.StartsWith("PCG.BRG:") || typeId.StartsWith("PCG.SpriteShapes:") ||
					typeId.StartsWith("PCG.Octree:") || typeId.StartsWith("PCG.Polygons:")));
			if (!mine)
				continue;
			diagCount++;
			sb.AppendLine($"{Prop(dg, "Code")} | type='{typeId}' | member='{Prop(dg, "MemberKey")}' | {Prop(dg, "Message")}");
		}
		if (diagCount == 0)
			sb.AppendLine("(none)");

		var summary = $"libs={libCount}, nodes={nodeCount}, incomplete={incomplete}, noExecutor={noExecutor}, diagnostics={diagCount}";
		sb.AppendLine();
		sb.AppendLine("=== SUMMARY ===");
		sb.AppendLine(summary);

		Debug.Log(sb.ToString());
		return summary;
	}

	private static object Prop(object obj, string name)
	{
		return obj.GetType().GetProperty(name).GetValue(obj);
	}
}
