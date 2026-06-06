using System;
using System.Collections.Generic;
using UnityEngine;
using PCG.Instances;
using PCG.Points;

namespace PCG.BRG
{
	/// <summary>
	/// Instance data for BatchRendererGroup instancing. Contains prefab and points grouped for efficient rendering.
	/// </summary>
	[Serializable]
	public class BrgInstanceData : InstanceData
	{
		/// <summary>
		/// Prefab to render in batch.
		/// </summary>
		public GameObject Prefab;
		/// <summary>
		/// List of points defining transform matrices for instances.
		/// </summary>
		public List<PointData> Points = new();
	}
}