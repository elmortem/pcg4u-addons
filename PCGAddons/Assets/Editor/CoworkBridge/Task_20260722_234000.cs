using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class Task_20260722_234000
{
	public static async Task<string> Run()
	{
		await Task.Yield();
		var text = new StringBuilder();
		var edit = GameObject.Find("Spline Edit");
		if (edit != null)
		{
			text.Append("Spline Edit:");
			foreach (var component in edit.GetComponents<Component>())
				text.Append(' ').Append(component.GetType().FullName);
		}
		else
		{
			text.Append("Spline Edit missing");
		}

		var filters = Resources.FindObjectsOfTypeAll<MeshFilter>();
		for (int i = 0; i < filters.Length; i++)
		{
			var filter = filters[i];
			if (filter == null || filter.sharedMesh == null || !filter.gameObject.scene.IsValid() || !filter.gameObject.name.StartsWith("Sweep"))
				continue;
			text.Append(" | path=");
			Transform current = filter.transform;
			while (current != null)
			{
				text.Append(current.name);
				var components = current.GetComponents<Component>();
				for (int c = 0; c < components.Length; c++)
				{
					string fullName = components[c].GetType().FullName;
					if (fullName.Contains("PCG") || fullName.Contains("Graph"))
						text.Append('[').Append(fullName).Append(']');
				}
				current = current.parent;
				if (current != null)
					text.Append("<-");
			}
			break;
		}
		return text.ToString();
	}
}
