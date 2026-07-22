using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class Task_20260722_235000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		var host = GameObject.Find("SweepGraph");
		if (host == null)
			return "SweepGraph missing";
		var text = new StringBuilder();
		foreach (var component in host.GetComponents<Component>())
		{
			Type type = component.GetType();
			text.Append(type.FullName).Append(':');
			foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(x => x.Name.IndexOf("gener", StringComparison.OrdinalIgnoreCase) >= 0 ||
					x.Name.IndexOf("comput", StringComparison.OrdinalIgnoreCase) >= 0 ||
					x.Name.IndexOf("clear", StringComparison.OrdinalIgnoreCase) >= 0 ||
					x.Name.IndexOf("refresh", StringComparison.OrdinalIgnoreCase) >= 0)
				.OrderBy(x => x.Name))
			{
				text.Append(' ').Append(method.Name).Append('(')
					.Append(string.Join(",", method.GetParameters().Select(x => x.ParameterType.Name)))
					.Append("):").Append(method.ReturnType.Name);
			}
			text.Append(" | ");
		}
		return text.ToString();
	}
}
