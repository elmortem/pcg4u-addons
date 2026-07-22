using System;
using System.Runtime.InteropServices;

namespace PCG.Sweep
{
	internal static class ManifoldNative
	{
		private const string Library = "manifoldc";

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_alloc_meshgl();

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_meshgl_w_options(
			IntPtr memory,
			IntPtr properties,
			UIntPtr vertexCount,
			UIntPtr propertyCount,
			IntPtr triangles,
			UIntPtr triangleCount,
			ref ManifoldMeshGLOptions options);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_alloc_manifold();

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_of_meshgl(IntPtr memory, IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern ManifoldError manifold_status(IntPtr manifold);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern int manifold_is_empty(IntPtr manifold);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_alloc_manifold_vec();

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_manifold_vec(IntPtr memory, UIntPtr count);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern void manifold_manifold_vec_set(IntPtr vector, UIntPtr index, IntPtr manifold);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_batch_boolean(IntPtr memory, IntPtr manifolds, ManifoldOpType operation);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_set_tolerance(IntPtr memory, IntPtr manifold, double tolerance);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_as_original(IntPtr memory, IntPtr manifold);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_get_meshgl(IntPtr memory, IntPtr manifold);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern UIntPtr manifold_meshgl_num_prop(IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern UIntPtr manifold_meshgl_vert_properties_length(IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern UIntPtr manifold_meshgl_tri_length(IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern UIntPtr manifold_meshgl_run_index_length(IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern UIntPtr manifold_meshgl_run_original_id_length(IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern UIntPtr manifold_meshgl_merge_length(IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_meshgl_vert_properties(IntPtr memory, IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_meshgl_tri_verts(IntPtr memory, IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_meshgl_run_index(IntPtr memory, IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_meshgl_run_original_id(IntPtr memory, IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_meshgl_merge_from_vert(IntPtr memory, IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern IntPtr manifold_meshgl_merge_to_vert(IntPtr memory, IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern uint manifold_reserve_ids(uint count);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern void manifold_delete_meshgl(IntPtr mesh);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern void manifold_delete_manifold(IntPtr manifold);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
		internal static extern void manifold_delete_manifold_vec(IntPtr vector);
	}
}
