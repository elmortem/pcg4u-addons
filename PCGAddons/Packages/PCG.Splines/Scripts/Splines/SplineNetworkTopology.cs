using System;
using System.Collections.Generic;

namespace PCG.Splines
{
	[Serializable]
	public sealed class SplineNetworkTopology
	{
		public List<SplineJunction> Junctions = new();
		public List<SplineCut> Cuts = new();

		public int GetContentHash()
		{
			unchecked
			{
				int hash = Junctions.Count;
				for (int i = 0; i < Junctions.Count; i++)
				{
					var junction = Junctions[i];
					hash = (hash * 397) ^ junction.Position.GetHashCode();
					hash = (hash * 397) ^ junction.Valency;
				}

				hash = (hash * 397) ^ Cuts.Count;
				for (int i = 0; i < Cuts.Count; i++)
				{
					var cut = Cuts[i];
					hash = (hash * 397) ^ cut.SplineIndex;
					hash = (hash * 397) ^ cut.CurveIndex;
					hash = (hash * 397) ^ cut.CurveT.GetHashCode();
					hash = (hash * 397) ^ cut.Distance.GetHashCode();
					hash = (hash * 397) ^ cut.Position.GetHashCode();
					hash = (hash * 397) ^ cut.JunctionIndex;
				}

				return hash;
			}
		}
	}
}
