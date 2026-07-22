using System.Linq;
using System.Threading.Tasks;
using PCG;
using PCG.Exec;
using PCG.Sweep;
using UnityEngine;

public static class Task_20260723_032000
{
	public static async Task<string> Run()
	{
		var host = GameObject.Find("SweepGraph");
		if (host == null)
			return "SweepGraph missing";

		var component = host.GetComponent<PcgComponent>();
		if (component == null)
			return "PcgComponent missing";

		var node = component.GraphData.Nodes.OfType<SweepSplineNode>().FirstOrDefault();
		if (node == null)
			return "SweepSplineNode missing";

		float modified = node.HeightOffset;
		node.HeightOffset = modified - 2f;
		bool generated = await PcgGraphRunner.GenerateAsync(component);

		int meshes = 0;
		int vertices = 0;
		float minY = float.PositiveInfinity;
		float maxY = float.NegativeInfinity;
		double sumY = 0d;
		foreach (var filter in host.GetComponentsInChildren<MeshFilter>(true))
		{
			if (filter.sharedMesh == null)
				continue;

			meshes++;
			foreach (Vector3 vertex in filter.sharedMesh.vertices)
			{
				float y = filter.transform.TransformPoint(vertex).y;
				vertices++;
				minY = Mathf.Min(minY, y);
				maxY = Mathf.Max(maxY, y);
				sumY += y;
			}
		}

		double averageY = vertices > 0 ? sumY / vertices : double.NaN;
		return $"generated={generated} modified={modified:F6} restored={node.HeightOffset:F6} meshes={meshes} vertices={vertices} minY={minY:F6} maxY={maxY:F6} avgY={averageY:F6}";
	}
}
