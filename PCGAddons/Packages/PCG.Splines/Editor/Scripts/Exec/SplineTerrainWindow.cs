using Unity.Mathematics;
using UnityEngine;

namespace PCG.Splines
{
	public sealed class SplineTerrainWindow
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

		public static SplineTerrainWindow Capture(TerrainData terrain, Vector3 origin, float worldMinX, float worldMaxX, float worldMinZ, float worldMaxZ)
		{
			int resolution = terrain.heightmapResolution;
			Vector3 size = terrain.size;

			float txMin = (worldMinX - origin.x) / size.x * (resolution - 1);
			float txMax = (worldMaxX - origin.x) / size.x * (resolution - 1);
			float tzMin = (worldMinZ - origin.z) / size.z * (resolution - 1);
			float tzMax = (worldMaxZ - origin.z) / size.z * (resolution - 1);

			int x0 = math.clamp((int)math.floor(txMin) - 1, 0, resolution - 1);
			int x1 = math.clamp((int)math.ceil(txMax) + 1, 0, resolution - 1);
			int z0 = math.clamp((int)math.floor(tzMin) - 1, 0, resolution - 1);
			int z1 = math.clamp((int)math.ceil(tzMax) + 1, 0, resolution - 1);

			int width = x1 - x0 + 1;
			int height = z1 - z0 + 1;

			var heights = terrain.GetHeights(x0, z0, width, height);

			return new SplineTerrainWindow
			{
				Heights = heights,
				X0 = x0,
				Z0 = z0,
				Width = width,
				Height = height,
				Resolution = resolution,
				SizeX = size.x,
				SizeY = size.y,
				SizeZ = size.z,
				OriginX = origin.x,
				OriginY = origin.y,
				OriginZ = origin.z
			};
		}

		public bool TrySampleHeight(float wx, float wz, out float height)
		{
			return TrySample(wx, wz, out height, out _);
		}

		public bool TrySampleNormal(float wx, float wz, out float3 normal)
		{
			return TrySample(wx, wz, out _, out normal);
		}

		public bool TrySample(float wx, float wz, out float height, out float3 normal)
		{
			height = 0f;
			normal = math.up();
			if (Width < 2 || Height < 2 || !(SizeX > 0f) || !(SizeZ > 0f))
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

			float normalizedDx = math.lerp(h10 - h00, h11 - h01, tz);
			float normalizedDz = math.lerp(h01 - h00, h11 - h10, tx);
			float heightDx = normalizedDx * SizeY * (Resolution - 1) / SizeX;
			float heightDz = normalizedDz * SizeY * (Resolution - 1) / SizeZ;
			normal = math.normalizesafe(new float3(-heightDx, 1f, -heightDz), math.up());
			return true;
		}
	}
}
