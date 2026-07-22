using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PCG.Sweep
{
	internal static class ManifoldBooleanAdapter
	{
		internal static uint ReserveIds(uint count)
		{
			return ManifoldNative.manifold_reserve_ids(count);
		}

		internal static bool TryUnion(
			IReadOnlyList<ManifoldBooleanInput> inputs,
			double simplifyTolerance,
			out ManifoldBooleanOutput output,
			out string failure)
		{
			output = null;
			failure = string.Empty;
			if (double.IsNaN(simplifyTolerance) || double.IsInfinity(simplifyTolerance) || simplifyTolerance < 0.0)
			{
				failure = "CallerToleranceInvalid";
				return false;
			}
			if (!ValidateInputs(inputs, out failure))
			{
				return false;
			}

			var handles = new List<GCHandle>(inputs.Count * 7 + 4);
			var meshes = new List<IntPtr>(inputs.Count);
			var sourceManifolds = new List<IntPtr>(inputs.Count);
			var manifolds = new List<IntPtr>(inputs.Count);
			IntPtr manifoldVector = IntPtr.Zero;
			IntPtr resultManifold = IntPtr.Zero;
			IntPtr outputMesh = IntPtr.Zero;
			try
			{
				for (int i = 0; i < inputs.Count; i++)
				{
					ManifoldBooleanInput input = inputs[i];
					IntPtr properties = Pin(input.Properties, handles);
					IntPtr triangles = Pin(input.Triangles, handles);
					var options = new ManifoldMeshGLOptions
					{
						RunIndices = Pin(input.RunIndices, handles),
						RunIndicesLength = Size(input.RunIndices.Length),
						RunOriginalIds = Pin(input.RunOriginalIds, handles),
						RunOriginalIdsLength = Size(input.RunOriginalIds.Length),
						MergeFromVertices = Pin(input.MergeFromVertices, handles),
						MergeToVertices = Pin(input.MergeToVertices, handles),
						MergeVerticesLength = Size(input.MergeFromVertices.Length),
						HalfedgeTangents = IntPtr.Zero
					};

					IntPtr meshMemory = ManifoldNative.manifold_alloc_meshgl();
					if (meshMemory == IntPtr.Zero)
					{
						failure = "InputMeshAllocationFailed-" + i;
						return false;
					}

					IntPtr mesh = ManifoldNative.manifold_meshgl_w_options(
						meshMemory,
						properties,
						Size(input.Properties.Length / input.PropertyCount),
						Size(input.PropertyCount),
						triangles,
						Size(input.Triangles.Length / 3),
						ref options);
					if (mesh == IntPtr.Zero)
					{
						failure = "InputMeshConstructionFailed-" + i;
						return false;
					}
					meshes.Add(mesh);

					IntPtr manifoldMemory = ManifoldNative.manifold_alloc_manifold();
					if (manifoldMemory == IntPtr.Zero)
					{
						failure = "InputManifoldAllocationFailed-" + i;
						return false;
					}

					IntPtr sourceManifold = ManifoldNative.manifold_of_meshgl(manifoldMemory, mesh);
					if (sourceManifold == IntPtr.Zero)
					{
						failure = "InputManifoldConstructionFailed-" + i;
						return false;
					}
					sourceManifolds.Add(sourceManifold);

					if (!ValidateManifold(sourceManifold, "InputManifold", i, out failure))
					{
						return false;
					}

					IntPtr toleranceMemory = ManifoldNative.manifold_alloc_manifold();
					if (toleranceMemory == IntPtr.Zero)
					{
						failure = "InputToleranceAllocationFailed-" + i;
						return false;
					}
					IntPtr manifold = ManifoldNative.manifold_set_tolerance(toleranceMemory, sourceManifold, simplifyTolerance);
					if (manifold == IntPtr.Zero)
					{
						failure = "InputToleranceConstructionFailed-" + i;
						return false;
					}
					manifolds.Add(manifold);
					if (!ValidateManifold(manifold, "InputTolerance", i, out failure))
					{
						return false;
					}
				}

				IntPtr vectorMemory = ManifoldNative.manifold_alloc_manifold_vec();
				if (vectorMemory == IntPtr.Zero)
				{
					failure = "ManifoldVectorAllocationFailed";
					return false;
				}
				manifoldVector = ManifoldNative.manifold_manifold_vec(vectorMemory, Size(manifolds.Count));
				if (manifoldVector == IntPtr.Zero)
				{
					failure = "ManifoldVectorConstructionFailed";
					return false;
				}
				for (int i = 0; i < manifolds.Count; i++)
				{
					ManifoldNative.manifold_manifold_vec_set(manifoldVector, Size(i), manifolds[i]);
				}

				IntPtr resultMemory = ManifoldNative.manifold_alloc_manifold();
				if (resultMemory == IntPtr.Zero)
				{
					failure = "ResultManifoldAllocationFailed";
					return false;
				}
				resultManifold = ManifoldNative.manifold_batch_boolean(
					resultMemory,
					manifoldVector,
					ManifoldOpType.Add);
				if (resultManifold == IntPtr.Zero)
				{
					failure = "ResultManifoldConstructionFailed";
					return false;
				}

				if (!ValidateManifold(resultManifold, "ResultManifold", out failure))
				{
					return false;
				}
				IntPtr outputMeshMemory = ManifoldNative.manifold_alloc_meshgl();
				if (outputMeshMemory == IntPtr.Zero)
				{
					failure = "OutputMeshAllocationFailed";
					return false;
				}
				outputMesh = ManifoldNative.manifold_get_meshgl(outputMeshMemory, resultManifold);
				if (outputMesh == IntPtr.Zero)
				{
					failure = "OutputMeshConstructionFailed";
					return false;
				}

				if (!TryReadOutput(outputMesh, handles, out ManifoldBooleanOutput candidate, out failure))
				{
					return false;
				}

				output = candidate;
				return true;
			}
			catch (Exception exception)
			{
				failure = "ManifoldNativeException-" + exception.GetType().Name + "-" + exception.Message;
				return false;
			}
			finally
			{
				if (outputMesh != IntPtr.Zero)
				{
					ManifoldNative.manifold_delete_meshgl(outputMesh);
				}
				if (resultManifold != IntPtr.Zero)
				{
					ManifoldNative.manifold_delete_manifold(resultManifold);
				}
				if (manifoldVector != IntPtr.Zero)
				{
					ManifoldNative.manifold_delete_manifold_vec(manifoldVector);
				}
				for (int i = manifolds.Count - 1; i >= 0; i--)
				{
					ManifoldNative.manifold_delete_manifold(manifolds[i]);
				}
				for (int i = sourceManifolds.Count - 1; i >= 0; i--)
				{
					ManifoldNative.manifold_delete_manifold(sourceManifolds[i]);
				}
				for (int i = meshes.Count - 1; i >= 0; i--)
				{
					ManifoldNative.manifold_delete_meshgl(meshes[i]);
				}
				for (int i = handles.Count - 1; i >= 0; i--)
				{
					handles[i].Free();
				}
			}
		}

		private static bool ValidateManifold(IntPtr manifold, string name, out string failure)
		{
			return ValidateManifold(manifold, name, -1, out failure);
		}

		private static bool ValidateManifold(IntPtr manifold, string name, int index, out string failure)
		{
			string suffix = index >= 0 ? "-" + index : string.Empty;
			ManifoldError status = ManifoldNative.manifold_status(manifold);
			if (status != ManifoldError.NoError)
			{
				failure = name + "Invalid" + suffix + "-" + status;
				return false;
			}
			if (ManifoldNative.manifold_is_empty(manifold) != 0)
			{
				failure = name + "Empty" + suffix;
				return false;
			}
			failure = string.Empty;
			return true;
		}

		private static bool ValidateInputs(IReadOnlyList<ManifoldBooleanInput> inputs, out string failure)
		{
			failure = string.Empty;
			if (inputs == null)
			{
				failure = "InputsNull";
				return false;
			}
			if (inputs.Count == 0)
			{
				failure = "InputsEmpty";
				return false;
			}

			for (int i = 0; i < inputs.Count; i++)
			{
				if (!ValidateInput(inputs[i], i, out failure))
				{
					return false;
				}
			}
			return true;
		}

		private static bool ValidateInput(ManifoldBooleanInput input, int inputIndex, out string failure)
		{
			failure = string.Empty;
			if (input == null)
			{
				failure = "InputNull-" + inputIndex;
				return false;
			}
			if (input.PropertyCount < 3)
			{
				failure = "InputPropertyCountInvalid-" + inputIndex;
				return false;
			}
			if (input.Properties == null || input.Properties.Length == 0 || input.Properties.Length % input.PropertyCount != 0)
			{
				failure = "InputPropertiesInvalid-" + inputIndex;
				return false;
			}
			if (input.Triangles == null || input.Triangles.Length == 0 || input.Triangles.Length % 3 != 0)
			{
				failure = "InputTrianglesInvalid-" + inputIndex;
				return false;
			}

			int vertexCount = input.Properties.Length / input.PropertyCount;
			uint vertexCountUnsigned = (uint)vertexCount;
			for (int i = 0; i < input.Triangles.Length; i++)
			{
				if (input.Triangles[i] >= vertexCountUnsigned)
				{
					failure = "InputTriangleIndexOutOfRange-" + inputIndex + "-" + i;
					return false;
				}
			}

			if (!ValidateRuns(input, inputIndex, out failure))
			{
				return false;
			}
			if (!ValidateMerges(input, inputIndex, vertexCount, out failure))
			{
				return false;
			}
			return true;
		}

		private static bool ValidateRuns(ManifoldBooleanInput input, int inputIndex, out string failure)
		{
			failure = string.Empty;
			if (input.RunIndices == null || input.RunOriginalIds == null)
			{
				failure = "InputRunsNull-" + inputIndex;
				return false;
			}
			if (input.RunIndices.Length == 0 && input.RunOriginalIds.Length == 0)
			{
				return true;
			}
			if (input.RunOriginalIds.Length == 0)
			{
				failure = "InputRunIdsEmpty-" + inputIndex;
				return false;
			}
			if (input.RunIndices.Length != input.RunOriginalIds.Length && input.RunIndices.Length != input.RunOriginalIds.Length + 1)
			{
				failure = "InputRunLengthsInvalid-" + inputIndex;
				return false;
			}
			if (input.RunIndices[0] != 0)
			{
				failure = "InputRunStartInvalid-" + inputIndex;
				return false;
			}

			uint triangleIndexCount = (uint)input.Triangles.Length;
			uint previous = 0;
			for (int i = 0; i < input.RunIndices.Length; i++)
			{
				uint current = input.RunIndices[i];
				if (current < previous || current > triangleIndexCount || current % 3 != 0)
				{
					failure = "InputRunIndexInvalid-" + inputIndex + "-" + i;
					return false;
				}
				previous = current;
			}
			if (input.RunIndices.Length == input.RunOriginalIds.Length + 1 && previous != triangleIndexCount)
			{
				failure = "InputRunEndInvalid-" + inputIndex;
				return false;
			}
			return true;
		}

		private static bool ValidateMerges(
			ManifoldBooleanInput input,
			int inputIndex,
			int vertexCount,
			out string failure)
		{
			failure = string.Empty;
			if (input.MergeFromVertices == null || input.MergeToVertices == null)
			{
				failure = "InputMergesNull-" + inputIndex;
				return false;
			}
			if (input.MergeFromVertices.Length != input.MergeToVertices.Length)
			{
				failure = "InputMergeLengthsInvalid-" + inputIndex;
				return false;
			}
			uint vertexCountUnsigned = (uint)vertexCount;
			for (int i = 0; i < input.MergeFromVertices.Length; i++)
			{
				if (input.MergeFromVertices[i] >= vertexCountUnsigned || input.MergeToVertices[i] >= vertexCountUnsigned)
				{
					failure = "InputMergeIndexOutOfRange-" + inputIndex + "-" + i;
					return false;
				}
			}
			return true;
		}

		private static bool TryReadOutput(
			IntPtr mesh,
			List<GCHandle> handles,
			out ManifoldBooleanOutput output,
			out string failure)
		{
			output = null;
			failure = string.Empty;
			if (!TryLength(ManifoldNative.manifold_meshgl_num_prop(mesh), "OutputPropertyCount", out int propertyCount, out failure))
			{
				return false;
			}
			if (!TryLength(ManifoldNative.manifold_meshgl_vert_properties_length(mesh), "OutputProperties", out int propertyLength, out failure))
			{
				return false;
			}
			if (!TryLength(ManifoldNative.manifold_meshgl_tri_length(mesh), "OutputTriangles", out int triangleLength, out failure))
			{
				return false;
			}
			if (!TryLength(ManifoldNative.manifold_meshgl_run_index_length(mesh), "OutputRunIndices", out int runIndexLength, out failure))
			{
				return false;
			}
			if (!TryLength(ManifoldNative.manifold_meshgl_run_original_id_length(mesh), "OutputRunIds", out int runIdLength, out failure))
			{
				return false;
			}
			if (!TryLength(ManifoldNative.manifold_meshgl_merge_length(mesh), "OutputMerges", out int mergeLength, out failure))
			{
				return false;
			}
			if (propertyCount < 3 || propertyLength == 0 || propertyLength % propertyCount != 0)
			{
				failure = "OutputPropertiesInvalid";
				return false;
			}
			if (triangleLength == 0 || triangleLength % 3 != 0)
			{
				failure = "OutputTrianglesInvalid";
				return false;
			}
			if (runIndexLength != runIdLength + 1)
			{
				failure = "OutputRunsInvalid";
				return false;
			}

			var candidate = new ManifoldBooleanOutput
			{
				PropertyCount = propertyCount,
				Properties = new float[propertyLength],
				Triangles = new uint[triangleLength],
				RunIndices = new uint[runIndexLength],
				RunOriginalIds = new uint[runIdLength],
				MergeFromVertices = new uint[mergeLength],
				MergeToVertices = new uint[mergeLength]
			};
			if (!CopyOutput(candidate.Properties, mesh, ManifoldNative.manifold_meshgl_vert_properties, handles))
			{
				failure = "OutputPropertiesCopyFailed";
				return false;
			}
			if (!CopyOutput(candidate.Triangles, mesh, ManifoldNative.manifold_meshgl_tri_verts, handles))
			{
				failure = "OutputTrianglesCopyFailed";
				return false;
			}
			if (!CopyOutput(candidate.RunIndices, mesh, ManifoldNative.manifold_meshgl_run_index, handles))
			{
				failure = "OutputRunIndicesCopyFailed";
				return false;
			}
			if (!CopyOutput(candidate.RunOriginalIds, mesh, ManifoldNative.manifold_meshgl_run_original_id, handles))
			{
				failure = "OutputRunIdsCopyFailed";
				return false;
			}
			if (!CopyOutput(candidate.MergeFromVertices, mesh, ManifoldNative.manifold_meshgl_merge_from_vert, handles))
			{
				failure = "OutputMergeFromCopyFailed";
				return false;
			}
			if (!CopyOutput(candidate.MergeToVertices, mesh, ManifoldNative.manifold_meshgl_merge_to_vert, handles))
			{
				failure = "OutputMergeToCopyFailed";
				return false;
			}
			output = candidate;
			return true;
		}

		private static bool CopyOutput<T>(
			T[] values,
			IntPtr mesh,
			Func<IntPtr, IntPtr, IntPtr> copy,
			List<GCHandle> handles)
			where T : struct
		{
			if (values.Length == 0)
			{
				return true;
			}
			IntPtr destination = Pin(values, handles);
			return copy(destination, mesh) != IntPtr.Zero;
		}

		private static bool TryLength(UIntPtr size, string name, out int length, out string failure)
		{
			ulong value = size.ToUInt64();
			if (value > int.MaxValue)
			{
				length = 0;
				failure = name + "TooLarge";
				return false;
			}
			length = (int)value;
			failure = string.Empty;
			return true;
		}

		private static IntPtr Pin(Array values, List<GCHandle> handles)
		{
			if (values.Length == 0)
			{
				return IntPtr.Zero;
			}
			GCHandle handle = GCHandle.Alloc(values, GCHandleType.Pinned);
			handles.Add(handle);
			return handle.AddrOfPinnedObject();
		}

		private static UIntPtr Size(int value)
		{
			return new UIntPtr((uint)value);
		}
	}
}
