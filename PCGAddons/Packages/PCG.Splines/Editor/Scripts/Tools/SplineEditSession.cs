using System.Collections.Generic;
using UnityEngine.Splines;

namespace PCG.Splines.Tools
{
	public sealed class SplineEditSession
	{
		public string Key;
		public string GraphId;
		public string AddressKey;
		public PcgSplineEditContainer Marker;
		public SplineContainer Container;
		public SplineNodeExecutor Executor;
		public List<Spline> Snapshot;
		public KnotLinkCollection SnapshotLinks;
		public bool Dirty;
		public int MatrixHash;
		public int LinksHash;
	}
}
