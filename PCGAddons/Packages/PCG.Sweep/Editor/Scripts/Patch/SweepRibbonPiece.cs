namespace PCG.Sweep
{
	internal struct SweepRibbonPiece
	{
		public const int Green = 0;
		public const int Red = 1;
		public const int Blue = 2;

		public int Spline;
		public float StartStation;
		public float EndStation;
		public int State;
	}
}
