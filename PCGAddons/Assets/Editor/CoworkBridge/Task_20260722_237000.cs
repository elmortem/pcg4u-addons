using System.Threading.Tasks;
using PCG;
using PCG.Exec;
using UnityEngine;

public static class Task_20260722_237000
{
	public static async Task<string> Run()
	{
		var host = GameObject.Find("SweepGraph");
		if (host == null)
			return "SweepGraph missing";

		var component = host.GetComponent<PcgComponent>();
		if (component == null)
			return "PcgComponent missing";

		bool generated = await PcgGraphRunner.GenerateAsync(component);
		return $"generated={generated}";
	}
}
