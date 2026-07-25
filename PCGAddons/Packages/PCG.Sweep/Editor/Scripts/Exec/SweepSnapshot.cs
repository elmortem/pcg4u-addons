using Unity.Mathematics;

namespace PCG.Sweep
{
	public sealed class SweepSnapshot
	{
		public float2[] ProfilePoints;
		public float[] ProfileUs;
		public int[] ProfileSegments;
		public bool ProfileClosed;

		public SweepFrame[][] Frames;
		public bool[] SplineClosed;

		public float[] WidthLut;
		public float[][] WidthLuts;
		public float[] HeightLut;
		public float[] TwistLut;

		public float MaxLateralExtent;
		public bool PreservePlanWidth;
		public float UvScale;
		public float HeightOffset;
		public bool[] CapStartFlags;
		public bool[] CapEndFlags;
		public bool Collider;
		public string Name;

		public float[] GetWidthLut(int splineIndex)
		{
			if (WidthLuts != null && splineIndex >= 0 && splineIndex < WidthLuts.Length && WidthLuts[splineIndex] != null)
				return WidthLuts[splineIndex];

			return WidthLut;
		}
	}
}
