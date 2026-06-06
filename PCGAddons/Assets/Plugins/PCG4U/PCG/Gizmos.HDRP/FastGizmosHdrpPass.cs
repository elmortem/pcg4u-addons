using UnityEngine.Rendering.HighDefinition;

namespace PCG.Fast.Hdrp
{
	public sealed class FastGizmosHdrpPass : CustomPass
	{
		public static FastGizmosHdrpBackend Backend;

		protected override void Execute(CustomPassContext ctx)
		{
			if (Backend == null)
			{
				return;
			}

			Backend.Draw(ctx);
		}
	}
}
