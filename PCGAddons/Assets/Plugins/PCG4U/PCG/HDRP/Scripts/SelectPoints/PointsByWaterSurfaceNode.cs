using System;
using System.Collections.Generic;
using PCG.GraphModel;
using PCG.Points;
using UnityEngine.Rendering.HighDefinition;

namespace PCG.SelectPoints
{
	[Serializable]
	[PcgNodeInfo("Separates points above and below an HDRP water surface level.",
		DisplayName = "Points By Water Surface",
		Category = "Select Points",
		Tags = new[] { "points", "select", "water", "surface", "height", "hdrp" })]
	public class PointsByWaterSurfaceNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Points to separate.", Tags = new[] { "points", "input" })]
		public List<PointData> Points = new();
		[Input]
		[PcgMemberInfo("HDRP water surface that defines the water level.", Tags = new[] { "water", "surface", "hdrp" })]
		public WaterSurface WaterSurface;
		[Input]
		[PcgMemberInfo("Vertical offset added to the water level.", Tags = new[] { "water", "height", "offset" })]
		public float Offset = 0f;

		[Output]
		[PcgMemberInfo("Points at or above the offset water level.", Tags = new[] { "points", "water", "above" })]
		public List<PointData> AboveWater => default;
		[Output]
		[PcgMemberInfo("Points below the offset water level.", Tags = new[] { "points", "water", "below" })]
		public List<PointData> BelowWater => default;
	}
}
