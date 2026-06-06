using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if PCG_URP_17
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace PCG.Fast.Urp
{
	public sealed class FastGizmosUrpPass : ScriptableRenderPass
	{
		private readonly FastGizmosUrpBackend _backend;

		public FastGizmosUrpPass(FastGizmosUrpBackend backend)
		{
			_backend = backend;
			renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
		}

#if PCG_URP_17
		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			using (var builder = renderGraph.AddUnsafePass<FastGizmosUrpPassData>("FastGizmos", out var passData))
			{
				var resourceData = frameData.Get<UniversalResourceData>();
				passData.Backend = _backend;
				passData.Color = resourceData.activeColorTexture;
				passData.Depth = resourceData.activeDepthTexture;
				builder.UseTexture(passData.Color, AccessFlags.Write);
				builder.UseTexture(passData.Depth, AccessFlags.Write);
				builder.AllowPassCulling(false);
				builder.SetRenderFunc((FastGizmosUrpPassData data, UnsafeGraphContext context) =>
				{
					var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
					cmd.SetRenderTarget(data.Color, data.Depth);
					data.Backend.Draw(cmd);
				});
			}
		}
#endif

#if !PCG_URP_17
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			var cmd = CommandBufferPool.Get("FastGizmos");
			_backend.Draw(cmd);
			context.ExecuteCommandBuffer(cmd);
			CommandBufferPool.Release(cmd);
		}
#endif
	}
}
