using Unity.Mathematics;

namespace PCG.Sweep
{
	public sealed class SweepTerrainWindow
	{
		public float[,] Heights;
		public int X0;
		public int Z0;
		public int Width;
		public int Height;
		public int Resolution;
		public float SizeX;
		public float SizeY;
		public float SizeZ;
		public float OriginX;
		public float OriginY;
		public float OriginZ;

		public bool TrySampleHeight(float wx, float wz, out float height)
		{
			height = 0f;
			if (Width < 2 || Height < 2)
				return false;

			float fx = (wx - OriginX) / SizeX * (Resolution - 1);
			float fz = (wz - OriginZ) / SizeZ * (Resolution - 1);
			if (fx < 0f || fz < 0f || fx > Resolution - 1 || fz > Resolution - 1)
				return false;

			float lx = fx - X0;
			float lz = fz - Z0;
			if (lx < 0f || lz < 0f || lx > Width - 1 || lz > Height - 1)
				return false;

			int x0 = math.min((int)math.floor(lx), Width - 2);
			int z0 = math.min((int)math.floor(lz), Height - 2);
			float tx = lx - x0;
			float tz = lz - z0;

			float h00 = Heights[z0, x0];
			float h10 = Heights[z0, x0 + 1];
			float h01 = Heights[z0 + 1, x0];
			float h11 = Heights[z0 + 1, x0 + 1];
			float h0 = math.lerp(h00, h10, tx);
			float h1 = math.lerp(h01, h11, tx);
			float normalized = math.lerp(h0, h1, tz);
			height = OriginY + normalized * SizeY;
			return true;
		}
	}
}
