using System.Collections.Generic;
using UnityEngine;

namespace PCG.Sweep
{
	internal sealed class SweepRibbonSplitResult
	{
		public List<Vector3> BlackPoints = new();
		public List<Vector3[]> CutChords = new();
		public List<Vector3[]> FreeSplines = new();
		public List<Vector3[]> DebugCuts = new();
		public List<int> DebugState = new();
		public List<SweepRibbonPiece> Pieces = new();
	}
}
