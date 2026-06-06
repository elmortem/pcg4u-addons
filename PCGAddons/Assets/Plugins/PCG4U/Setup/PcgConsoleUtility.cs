using System.Reflection;
using UnityEditor;

namespace PCG.Setup
{
	public static class PcgConsoleUtility
	{
		public static void Clear()
		{
			var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
			if (type == null)
				return;
			var method = type.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
			if (method == null)
				return;
			method.Invoke(null, null);
		}
	}
}
