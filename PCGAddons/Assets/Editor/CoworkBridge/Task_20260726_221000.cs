using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;

public static class Task_20260726_221000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		var sb = new StringBuilder();
		var request = Client.List(true, false);
		while (!request.IsCompleted)
		{
			await Task.Delay(200);
		}

		if (request.Status != StatusCode.Success)
		{
			sb.AppendLine("PackageManager list failed: " + request.Error.message);
		}
		else
		{
			foreach (var package in request.Result.Where(p => p.name.StartsWith("com.elmortem")))
			{
				sb.AppendLine(package.name + " " + package.version + " source=" + package.source
					+ " errors=" + (package.errors != null ? package.errors.Length : 0));
				if (package.errors != null)
				{
					foreach (var error in package.errors)
					{
						sb.AppendLine("    " + error.errorCode + ": " + error.message);
					}
				}
			}
		}

		Debug.Log(sb.ToString());
		return sb.ToString();
	}
}
