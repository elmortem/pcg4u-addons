using UnityEditor;
using UnityEngine.Rendering.HighDefinition;

namespace PCG.Fast.Hdrp
{
	public static class FastGizmosHdrpBootstrap
	{
		[InitializeOnLoadMethod]
		private static void Register()
		{
			FastGizmosBackendRegistry.Register(
				asset => asset is HDRenderPipelineAsset,
				() => new FastGizmosHdrpBackend());
		}
	}
}
