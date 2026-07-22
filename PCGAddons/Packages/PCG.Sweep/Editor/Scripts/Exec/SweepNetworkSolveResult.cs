using System.Collections.Generic;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	public sealed class SweepNetworkSolveResult
	{
		public List<Spline> PieceSplines;
		public float[] RangeStart;
		public float[] RangeEnd;
		public bool[] FreeStart;
		public bool[] FreeEnd;
		public bool[] PieceClosed;
		public float[] SourceLength;
		public float[] PieceStartDistance;
		public int[] JunctionComponents;
		public SweepNetworkJunction[] Junctions;
	}
}
