using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

public static class Task_20260722_236000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		var text = new StringBuilder();
		var types = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(assembly =>
			{
				try
				{
					return assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException exception)
				{
					return exception.Types.Where(type => type != null).ToArray();
				}
			})
			.Where(type => type != null &&
				(type.Name.IndexOf("GraphRunner", StringComparison.OrdinalIgnoreCase) >= 0 ||
				 type.Name.IndexOf("AutoGenerate", StringComparison.OrdinalIgnoreCase) >= 0 ||
				 type.Name.IndexOf("GraphWindow", StringComparison.OrdinalIgnoreCase) >= 0))
			.OrderBy(type => type.FullName);

		foreach (var type in types)
		{
			text.Append(type.FullName).Append(':');
			foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(method => method.Name.IndexOf("gener", StringComparison.OrdinalIgnoreCase) >= 0 ||
					method.Name.IndexOf("clear", StringComparison.OrdinalIgnoreCase) >= 0 ||
					method.Name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0)
				.OrderBy(method => method.Name))
			{
				text.Append(' ').Append(method.IsStatic ? "static " : string.Empty).Append(method.Name).Append('(')
					.Append(string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName)))
					.Append("):").Append(method.ReturnType.FullName);
			}
			text.Append(" | ");
		}

		return text.ToString();
	}
}
