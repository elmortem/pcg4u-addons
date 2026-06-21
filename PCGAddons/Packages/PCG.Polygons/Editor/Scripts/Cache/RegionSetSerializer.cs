using System;
using System.IO;
using System.Runtime.InteropServices;
using PCG.Cache;
using PCG.Cache.Serializers;
using Unity.Mathematics;

namespace PCG.Polygons
{
	public sealed class RegionSetSerializer : IPcgCacheSerializer
	{
		private const int ChunkBytes = 65536;

		public int TypeId => 2;

		public bool CanHandle(Type type)
		{
			return type == typeof(RegionSet);
		}

		public object Snapshot(object value)
		{
			return ((RegionSet)value).Clone();
		}

		public void Write(BinaryWriter writer, object value)
		{
			var set = (RegionSet)value;
			writer.Write(set.PlaneY);
			writer.Write(set.Regions.Count);
			for (int i = 0; i < set.Regions.Count; i++)
			{
				var region = set.Regions[i];
				WriteRing(writer, region.Outer);
				writer.Write(region.Holes.Count);
				for (int h = 0; h < region.Holes.Count; h++)
				{
					WriteRing(writer, region.Holes[h]);
				}

				PcgAttributeSetCacheIO.Write(writer, region.EdgeAttributes);
			}

			PcgAttributeSetCacheIO.Write(writer, set.Attributes);
		}

		public object Read(BinaryReader reader, Type type)
		{
			var set = new RegionSet();
			set.PlaneY = reader.ReadSingle();
			int regionCount = reader.ReadInt32();
			for (int i = 0; i < regionCount; i++)
			{
				var region = new Polygon2D();
				region.Outer = ReadRing(reader);
				int holeCount = reader.ReadInt32();
				for (int h = 0; h < holeCount; h++)
				{
					region.Holes.Add(ReadRing(reader));
				}

				var edgeAttributes = PcgAttributeSetCacheIO.Read(reader);
				region.EdgeAttributes.Append(edgeAttributes);
				set.Regions.Add(region);
			}

			var attributes = PcgAttributeSetCacheIO.Read(reader);
			set.Attributes.Append(attributes);
			return set;
		}

		private static void WriteRing(BinaryWriter writer, float2[] ring)
		{
			int count = ring != null ? ring.Length : 0;
			writer.Write(count);
			if (count == 0)
				return;

			var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<float2>(ring));
			WriteBlob(writer, bytes);
		}

		private static float2[] ReadRing(BinaryReader reader)
		{
			int count = reader.ReadInt32();
			if (count == 0)
				return Array.Empty<float2>();

			var ring = new float2[count];
			var bytes = MemoryMarshal.AsBytes(new Span<float2>(ring));
			ReadBlob(reader, bytes);
			return ring;
		}

		private static void WriteBlob(BinaryWriter writer, ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length == 0)
				return;

			var buffer = new byte[Math.Min(ChunkBytes, bytes.Length)];
			for (int i = 0; i < bytes.Length; i += buffer.Length)
			{
				int n = Math.Min(buffer.Length, bytes.Length - i);
				bytes.Slice(i, n).CopyTo(buffer);
				writer.Write(buffer, 0, n);
			}
		}

		private static void ReadBlob(BinaryReader reader, Span<byte> bytes)
		{
			if (bytes.Length == 0)
				return;

			var buffer = new byte[Math.Min(ChunkBytes, bytes.Length)];
			int offset = 0;
			while (offset < bytes.Length)
			{
				int count = Math.Min(buffer.Length, bytes.Length - offset);
				int read = reader.Read(buffer, 0, count);
				if (read <= 0)
					throw new EndOfStreamException();

				new ReadOnlySpan<byte>(buffer, 0, read).CopyTo(bytes.Slice(offset, read));
				offset += read;
			}
		}
	}
}
