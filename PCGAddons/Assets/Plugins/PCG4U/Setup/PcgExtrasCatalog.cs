using System.Collections.Generic;
using UnityEngine;

namespace PCG.Setup
{
	//[CreateAssetMenu(menuName = "PCG/Extras Catalog", fileName = "PcgExtrasCatalog")]
	public class PcgExtrasCatalog : ScriptableObject
	{
		public List<PcgExtrasPackageEntry> Packages = new();
	}
}
