using System;
using System.Runtime.InteropServices;

namespace PCG.Sweep
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct ManifoldMeshGLOptions
	{
		internal IntPtr RunIndices;
		internal UIntPtr RunIndicesLength;
		internal IntPtr RunOriginalIds;
		internal UIntPtr RunOriginalIdsLength;
		internal IntPtr MergeFromVertices;
		internal IntPtr MergeToVertices;
		internal UIntPtr MergeVerticesLength;
		internal IntPtr HalfedgeTangents;
	}
}
