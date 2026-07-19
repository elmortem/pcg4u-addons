namespace PCG.Splines
{
	public sealed class SplineIntersectionResult
	{
		public SplineNetworkTopology Topology = new();
		public bool ToleranceNotReached;
		public bool CollinearOverlap;
	}
}
