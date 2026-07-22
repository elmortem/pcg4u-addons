using System.Collections.Generic;

namespace PCG.Splines
{
	public sealed class SplineSplitResult
	{
		public List<List<KnotInstruction>>[] Pieces;
		public List<SplinePieceIncidence>[] PieceIncidence;
		public bool EmbeddedDataWarning;
		public bool InvalidValues;
	}
}
