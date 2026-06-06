#if PCG_URP_17
using UnityEngine.Rendering.RenderGraphModule;

namespace PCG.Fast.Urp
{
	public sealed class FastGizmosUrpPassData
	{
		public FastGizmosUrpBackend Backend;
		public TextureHandle Color;
		public TextureHandle Depth;
	}
}
#endif
