using System;

namespace PCG.Sweep
{
	internal sealed class ManifoldBooleanInput
	{
		internal float[] Properties = Array.Empty<float>();
		internal int PropertyCount;
		internal uint[] Triangles = Array.Empty<uint>();
		internal uint[] RunIndices = Array.Empty<uint>();
		internal uint[] RunOriginalIds = Array.Empty<uint>();
		internal uint[] MergeFromVertices = Array.Empty<uint>();
		internal uint[] MergeToVertices = Array.Empty<uint>();
	}
}
