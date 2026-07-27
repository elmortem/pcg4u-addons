using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PCG;
using PCG.Exec;
using PCG.SubGraphs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Task_20260726_214000
{
	private const string ScenePath = "Assets/SweepDemo/SweepDemoScene.unity";

	private static readonly StringBuilder Proof = new StringBuilder();

	public static async Task<string> Run()
	{
		var sb = new StringBuilder();
		EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		await WaitIdle(20f);

		var components = UnityEngine.Object.FindObjectsOfType<PcgComponent>(true);
		sb.AppendLine("=== SCENE " + ScenePath + " : " + components.Length + " PcgComponent(s) ===");

		var lines = new List<string>();
		foreach (var component in components)
		{
			var graph = PcgGraphRunner.GetGraph(component);
			if (graph == null)
			{
				lines.Add(component.GraphId + "|<no graph>|-|-|-");
				continue;
			}

			await ResolveGraph(graph, component.GraphId, lines);
		}

		lines.Sort(StringComparer.Ordinal);
		foreach (var line in lines)
		{
			sb.AppendLine(line);
		}

		System.IO.File.WriteAllText("Docs/notes/_post_sweep.txt", sb.ToString());

		var registryType = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(SafeTypes)
			.FirstOrDefault(t => t.FullName == "PCG.Cache.PcgCacheSerializerRegistry");
		var splineSetType = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(SafeTypes)
			.FirstOrDefault(t => t.FullName == "PCG.Splines.PcgSplineSet");
		var forType = registryType.GetMethod("ForType", BindingFlags.Public | BindingFlags.Static);
		var serializer = forType.Invoke(null, new object[] { splineSetType });
		Proof.AppendLine("REGISTRY ForType(PcgSplineSet) = " + (serializer != null ? serializer.GetType().FullName : "NULL"));

		System.IO.File.WriteAllText("Docs/notes/_proof_sweep.txt", Proof.ToString());
		Debug.Log(Proof.ToString());
		return "post capture + proof written";
	}

	private static IEnumerable<Type> SafeTypes(Assembly a)
	{
		try
		{
			return a.GetTypes();
		}
		catch
		{
			return Type.EmptyTypes;
		}
	}

	private static async UniTask ResolveGraph(PcgExecGraph graph, string idPath, List<string> lines)
	{
		var executors = graph.Executors.ToList();
		foreach (var executor in executors)
		{
			try
			{
				await PcgComputeSystem.ResolveAsync(executor, CancellationToken.None);
			}
			catch (Exception e)
			{
				lines.Add(idPath + "|" + Title(graph, executor) + "|" + executor.GetType().Name + "|-|Error:" + e.GetType().Name);
			}
		}

		foreach (var executor in executors)
		{
			var title = Title(graph, executor);
			var typeName = executor.GetType().Name;
			bool isProofNode = typeName == "BlocksToRoadsNodeExecutor"
				|| typeName == "PointsOffsetSplinesNodeExecutor"
				|| typeName == "SplineIntersectionNodeExecutor";

			foreach (var port in graph.Outputs(executor))
			{
				object value = null;
				try
				{
					value = executor.GetValue(port);
				}
				catch (Exception e)
				{
					lines.Add(idPath + "|" + title + "|" + typeName + "|" + port.FieldName + "|Error:" + e.GetType().Name);
					continue;
				}

				var described = Describe(value);
				if (described != null)
				{
					foreach (var d in described)
					{
						lines.Add(idPath + "|" + title + "|" + typeName + "|" + port.FieldName + "|" + d);
					}
				}

				if (isProofNode)
					DumpProof(idPath, title, typeName, port.FieldName, value);
			}
		}

		foreach (var executor in executors)
		{
			if (executor is SubGraphNodeExecutor subGraph && subGraph.Inner != null)
			{
				await ResolveGraph(subGraph.Inner, idPath + ">" + Title(graph, executor), lines);
			}
		}
	}

	private static void DumpProof(string idPath, string title, string typeName, string port, object value)
	{
		if (value == null)
			return;

		var type = value.GetType();
		if (type.Name != "PcgSplineSet" && type.Name != "PcgPointCloud")
			return;

		var attributes = type.GetProperty("Attributes").GetValue(value);
		int count = (int)type.GetProperty("Count").GetValue(value);
		Proof.AppendLine("### " + idPath + " | " + title + " (" + typeName + ") ." + port + " count=" + count
			+ " IsValid=" + type.GetMethod("IsValid").Invoke(value, null));
		Proof.AppendLine("    columns: " + Columns(attributes));
		var columns = attributes.GetType().GetProperty("Columns").GetValue(attributes) as IEnumerable;
		int rows = Math.Min(3, count);
		for (int r = 0; r < rows; r++)
		{
			var parts = new List<string>();
			foreach (var kv in columns)
			{
				var key = kv.GetType().GetProperty("Key").GetValue(kv) as string;
				var column = kv.GetType().GetProperty("Value").GetValue(kv);
				var boxed = column.GetType().GetMethod("GetBoxed").Invoke(column, new object[] { r });
				parts.Add(key + "=" + boxed);
			}

			parts.Sort(StringComparer.Ordinal);
			Proof.AppendLine("    row " + r + ": " + string.Join(", ", parts));
		}

		if (typeName == "SplineIntersectionNodeExecutor" && port == "Results")
		{
			var distinct = new SortedSet<string>();
			foreach (var kv in columns)
			{
				var key = kv.GetType().GetProperty("Key").GetValue(kv) as string;
				if (key != "junctionValency")
					continue;

				var column = kv.GetType().GetProperty("Value").GetValue(kv);
				var getBoxed = column.GetType().GetMethod("GetBoxed");
				for (int r = 0; r < count; r++)
				{
					distinct.Add(getBoxed.Invoke(column, new object[] { r }).ToString());
				}
			}

			Proof.AppendLine("    junctionValency distinct values: " + string.Join(",", distinct));
		}
	}

	private static string Title(PcgExecGraph graph, PcgNodeExecutor executor)
	{
		try
		{
			return graph.GetTitle(executor);
		}
		catch
		{
			return executor.GetType().Name;
		}
	}

	private static List<string> Describe(object value)
	{
		if (value == null)
			return null;

		var result = new List<string>();
		var type = value.GetType();
		var typeName = type.Name;

		if (typeName == "PcgPointCloud")
		{
			result.Add("Points:" + (int)type.GetProperty("Count").GetValue(value));
			result.Add("Attrs:" + Columns(type.GetProperty("Attributes").GetValue(value)));
			result.Add("IsValid:" + type.GetMethod("IsValid").Invoke(value, null));
			return result;
		}

		if (typeName == "PcgSplineSet")
		{
			result.Add("Splines:" + (int)type.GetProperty("Count").GetValue(value));
			result.Add("Attrs:" + Columns(type.GetProperty("Attributes").GetValue(value)));
			result.Add("IsValid:" + type.GetMethod("IsValid").Invoke(value, null));
			return result;
		}

		if (typeName == "RegionSet")
		{
			result.Add("Regions:" + (int)type.GetProperty("Count").GetValue(value));
			result.Add("Attrs:" + Columns(type.GetProperty("Attributes").GetValue(value)));
			return result;
		}

		if (typeName == "SplineNetworkTopology")
		{
			var junctions = type.GetField("Junctions").GetValue(value) as IList;
			var cuts = type.GetField("Cuts").GetValue(value) as IList;
			result.Add("Junctions:" + (junctions != null ? junctions.Count : 0) + ",Cuts:" + (cuts != null ? cuts.Count : 0));
			return result;
		}

		if (value is IList list)
		{
			var elementType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
			if (elementType.Name == "Spline")
			{
				result.Add("Splines:" + list.Count);
				return result;
			}

			if (elementType.Name == "MeshInstanceData")
			{
				long verts = 0;
				long tris = 0;
				for (int i = 0; i < list.Count; i++)
				{
					var item = list[i];
					if (item == null)
						continue;

					verts += Len(item, "Vertices");
					tris += Len(item, "Triangles");
				}

				result.Add("Meshes:" + list.Count + ",Verts:" + verts + ",Tris:" + tris);
				return result;
			}

			return null;
		}

		return null;
	}

	private static long Len(object item, string fieldName)
	{
		var t = item.GetType();
		var f = t.GetField(fieldName);
		object v = f != null ? f.GetValue(item) : null;
		if (v == null)
		{
			var p = t.GetProperty(fieldName);
			v = p != null ? p.GetValue(item) : null;
		}

		if (v is Array arr)
			return arr.Length;

		if (v is ICollection col)
			return col.Count;

		return 0;
	}

	private static string Columns(object attributeSet)
	{
		if (attributeSet == null)
			return "";

		var columns = attributeSet.GetType().GetProperty("Columns").GetValue(attributeSet) as IEnumerable;
		if (columns == null)
			return "";

		var names = new List<string>();
		foreach (var kv in columns)
		{
			names.Add(kv.GetType().GetProperty("Key").GetValue(kv) as string);
		}

		names.Sort(StringComparer.Ordinal);
		return string.Join(",", names);
	}

	private static async UniTask WaitIdle(float seconds)
	{
		var deadline = DateTime.UtcNow.AddSeconds(seconds);
		int stable = 0;
		while (DateTime.UtcNow < deadline)
		{
			await UniTask.Delay(200, DelayType.Realtime);
			if (!PcgComputeSystem.IsBusy && !PcgComputeSystem.IsGenerating)
			{
				stable++;
				if (stable >= 5)
					return;
			}
			else
			{
				stable = 0;
			}
		}
	}
}
