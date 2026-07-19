using UnityEngine.Splines;

namespace PCG.Splines
{
	public struct KnotInstruction
	{
		public BezierKnot Knot;
		public TangentMode Mode;
		public float Tension;
	}
}
