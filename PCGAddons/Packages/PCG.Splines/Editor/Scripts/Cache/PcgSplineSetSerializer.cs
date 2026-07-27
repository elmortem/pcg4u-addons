using System;
using System.IO;
using PCG.Cache;
using PCG.Cache.Serializers;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public sealed class PcgSplineSetSerializer : IPcgCacheSerializer
	{
		public int TypeId => 4;

		public bool CanHandle(Type type)
		{
			return type == typeof(PcgSplineSet);
		}

		public object Snapshot(object value)
		{
			return ((PcgSplineSet)value).Clone();
		}

		public void Write(BinaryWriter writer, object value)
		{
			var set = (PcgSplineSet)value;
			writer.Write(set.Splines.Count);
			bool warned = false;
			for (int i = 0; i < set.Splines.Count; i++)
			{
				var spline = set.Splines[i];
				if (spline == null)
				{
					writer.Write(false);
					writer.Write(0);
					writer.Write(0);
					continue;
				}

				writer.Write(spline.Closed);
				writer.Write(spline.Count);
				for (int k = 0; k < spline.Count; k++)
				{
					var knot = spline[k];
					writer.Write(knot.Position.x);
					writer.Write(knot.Position.y);
					writer.Write(knot.Position.z);
					writer.Write(knot.TangentIn.x);
					writer.Write(knot.TangentIn.y);
					writer.Write(knot.TangentIn.z);
					writer.Write(knot.TangentOut.x);
					writer.Write(knot.TangentOut.y);
					writer.Write(knot.TangentOut.z);
					writer.Write(knot.Rotation.value.x);
					writer.Write(knot.Rotation.value.y);
					writer.Write(knot.Rotation.value.z);
					writer.Write(knot.Rotation.value.w);
					writer.Write((byte)spline.GetTangentMode(k));
					writer.Write(spline.GetAutoSmoothTension(k));
				}

				WriteFloatChannels(writer, spline);
				if (!warned)
					warned = WarnUnsupportedChannels(spline);
			}

			PcgAttributeSetCacheIO.Write(writer, set.Attributes);
		}

		public object Read(BinaryReader reader, Type type)
		{
			var set = new PcgSplineSet();
			int splineCount = reader.ReadInt32();
			for (int i = 0; i < splineCount; i++)
			{
				bool closed = reader.ReadBoolean();
				int knotCount = reader.ReadInt32();
				var knots = new BezierKnot[knotCount];
				var modes = new TangentMode[knotCount];
				var tensions = new float[knotCount];
				for (int k = 0; k < knotCount; k++)
				{
					var position = new Unity.Mathematics.float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					var tangentIn = new Unity.Mathematics.float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					var tangentOut = new Unity.Mathematics.float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					var rotation = new Unity.Mathematics.quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					knots[k] = new BezierKnot(position, tangentIn, tangentOut, rotation);
					modes[k] = (TangentMode)reader.ReadByte();
					tensions[k] = reader.ReadSingle();
				}

				var spline = new Spline(knots, closed);
				for (int k = 0; k < knotCount; k++)
				{
					spline.SetTangentModeNoNotify(k, modes[k]);
					spline.SetAutoSmoothTensionNoNotify(k, tensions[k]);
				}

				ReadFloatChannels(reader, spline);
				set.Splines.Add(spline);
			}

			var attributes = PcgAttributeSetCacheIO.Read(reader);
			set.Attributes.Append(attributes);
			return set;
		}

		private static void WriteFloatChannels(BinaryWriter writer, Spline spline)
		{
			int count = 0;
			foreach (var key in spline.GetFloatDataKeys())
			{
				if (spline.TryGetFloatData(key, out var data) && data != null)
					count++;
			}

			writer.Write(count);
			foreach (var key in spline.GetFloatDataKeys())
			{
				if (!spline.TryGetFloatData(key, out var data) || data == null)
					continue;

				writer.Write(key);
				writer.Write((byte)data.PathIndexUnit);
				writer.Write(data.DefaultValue);
				writer.Write(data.Count);
				foreach (var point in data)
				{
					writer.Write(point.Index);
					writer.Write(point.Value);
				}
			}
		}

		private static void ReadFloatChannels(BinaryReader reader, Spline spline)
		{
			int channelCount = reader.ReadInt32();
			for (int c = 0; c < channelCount; c++)
			{
				string key = reader.ReadString();
				var data = new SplineData<float>
				{
					PathIndexUnit = (PathIndexUnit)reader.ReadByte(),
					DefaultValue = reader.ReadSingle()
				};

				int pointCount = reader.ReadInt32();
				for (int p = 0; p < pointCount; p++)
				{
					float index = reader.ReadSingle();
					float value = reader.ReadSingle();
					data.Add(index, value);
				}

				spline.SetFloatData(key, data);
			}
		}

		private static bool WarnUnsupportedChannels(Spline spline)
		{
			foreach (var key in spline.GetFloat4DataKeys())
			{
				Debug.LogWarning($"PcgSplineSetSerializer: float4 spline channel '{key}' is not cached.");
				return true;
			}

			foreach (var key in spline.GetIntDataKeys())
			{
				Debug.LogWarning($"PcgSplineSetSerializer: int spline channel '{key}' is not cached.");
				return true;
			}

			foreach (var key in spline.GetObjectDataKeys())
			{
				Debug.LogWarning($"PcgSplineSetSerializer: object spline channel '{key}' is not cached.");
				return true;
			}

			return false;
		}
	}
}
