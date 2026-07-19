using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines.Tools
{
	public sealed class PcgSplineEditContainer : MonoBehaviour, IPcgTempRoot
	{
		public SplineContainer Container;
		public string GraphId;
		public string AddressKey;
	}
}
