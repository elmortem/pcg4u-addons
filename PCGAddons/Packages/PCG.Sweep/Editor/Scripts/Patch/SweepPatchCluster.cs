using System.Collections.Generic;

namespace PCG.Sweep
{
	internal sealed class SweepPatchCluster
	{
		public int Index;
		public List<int> Hits = new();
		public int[] ArmSpline;
		public float[] CutStart;
		public float[] CutEnd;
		public bool[] AbsorbedStart;
		public bool[] AbsorbedEnd;

		public int ArmCount => ArmSpline.Length;

		public int ArmOf(int splineIndex, float station, float tolerance)
		{
			for (int i = 0; i < ArmSpline.Length; i++)
			{
				if (ArmSpline[i] != splineIndex)
					continue;

				if (station >= CutStart[i] - tolerance && station <= CutEnd[i] + tolerance)
					return i;
			}
			return -1;
		}
	}
}
