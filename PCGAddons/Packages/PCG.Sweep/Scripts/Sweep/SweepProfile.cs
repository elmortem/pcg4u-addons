using Unity.Mathematics;

namespace PCG.Sweep
{
	public sealed class SweepProfile
	{
		public float2[] Points;
		public float[] Us;
		public int[] Segments;
		public bool Closed;

		public int GetContentHash()
		{
			unchecked
			{
				int hash = 17;
				hash = (hash * 397) ^ Points.Length;
				for (int i = 0; i < Points.Length; i++)
				{
					hash = (hash * 397) ^ Points[i].x.GetHashCode();
					hash = (hash * 397) ^ Points[i].y.GetHashCode();
				}
				for (int i = 0; i < Us.Length; i++)
					hash = (hash * 397) ^ Us[i].GetHashCode();
				hash = (hash * 397) ^ Segments.Length;
				for (int i = 0; i < Segments.Length; i++)
					hash = (hash * 397) ^ Segments[i];
				hash = (hash * 397) ^ Closed.GetHashCode();
				return hash;
			}
		}
	}
}
