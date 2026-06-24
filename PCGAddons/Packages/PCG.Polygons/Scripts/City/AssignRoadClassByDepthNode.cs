using PCG.GraphModel;
using UnityEngine;

namespace PCG.Polygons.City
{
	public sealed class AssignRoadClassByDepthNode : PcgPreviewNode
	{
		[Input]
		public RegionSet Blocks;

		public AnimationCurve WidthByDepth = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);

		[Input]
		public float MaxWidth = 8f;

		[Input]
		public int MinDepth = 1;

		[Input]
		public int MaxDepth = 6;

		[Output]
		public RegionSet Result => default;
	}
}
