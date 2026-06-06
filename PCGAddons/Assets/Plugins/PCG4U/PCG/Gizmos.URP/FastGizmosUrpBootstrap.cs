using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace PCG.Fast.Urp
{
	public static class FastGizmosUrpBootstrap
	{
		[InitializeOnLoadMethod]
		private static void Register()
		{
			FastGizmosBackendRegistry.Register(
				asset => asset is UniversalRenderPipelineAsset,
				() => new FastGizmosUrpBackend());
		}
	}
}
