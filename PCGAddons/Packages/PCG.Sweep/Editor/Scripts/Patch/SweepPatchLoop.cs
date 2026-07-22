using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepPatchLoop
	{
		public float3[] Points;
		public float2[] Plan;

		public float SignedArea()
		{
			float area = 0f;
			for (int i = 0; i < Plan.Length; i++)
			{
				float2 a = Plan[i];
				float2 b = Plan[(i + 1) % Plan.Length];
				area += a.x * b.y - b.x * a.y;
			}
			return area * 0.5f;
		}
	}
}
