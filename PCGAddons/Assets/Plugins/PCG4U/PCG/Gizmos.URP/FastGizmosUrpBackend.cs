using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PCG.Fast.Urp
{
	public sealed class FastGizmosUrpBackend : IFastGizmosRenderBackend
	{
		private FastGizmosBackendContext _context;
		private Material _material;
		private FastGizmosUrpPass _pass;

		public void Initialize(FastGizmosBackendContext context)
		{
			_context = context;
			_material = new Material(Shader.Find("PCG4U/FastGizmosShapeUrp"));
			_material.SetVector("_LightDirection", new Vector4(0.1f, -0.7f, 0.3f, 0));
			_material.SetFloat("_ShadowStrength", 0.6f);
			_pass = new FastGizmosUrpPass(this);
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
		}

		public void UpdateBranch(GizmoRenderData gizmoData, BranchRenderData branch, Camera sceneCamera)
		{
			if (branch.PropertyBlock == null)
			{
				branch.PropertyBlock = new MaterialPropertyBlock();
			}

			branch.PropertyBlock.SetBuffer("_Matrices", branch.MatricesBuffer);
			branch.PropertyBlock.SetBuffer("_Colors", branch.ColorsBuffer);
		}

		public void RemoveBranch(BranchRenderData branch, Camera sceneCamera)
		{
		}

		public void OnSceneCameraChanged(Camera oldCamera, Camera newCamera)
		{
		}

		public void Shutdown()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			if (_material != null)
			{
				Object.DestroyImmediate(_material);
			}
		}

		internal void Draw(CommandBuffer cmd)
		{
			foreach (var gizmoData in _context.GetGizmoDatas())
			{
				if (gizmoData.Shape == null)
				{
					continue;
				}

				var mesh = gizmoData.Shape.GetMesh();
				foreach (var branch in gizmoData.BranchBuffers)
				{
					if (branch.PropertyBlock == null || branch.ArgsBuffer == null || branch.Count == 0)
					{
						continue;
					}

					cmd.DrawMeshInstancedIndirect(mesh, 0, _material, 0, branch.ArgsBuffer, 0, branch.PropertyBlock);
				}
			}
		}

		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera == null || camera != _context.GetSceneCamera())
			{
				return;
			}

			if (!HasBranches())
			{
				return;
			}

			camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_pass);
		}

		private bool HasBranches()
		{
			foreach (var gizmoData in _context.GetGizmoDatas())
			{
				if (gizmoData.BranchBuffers.Count > 0)
				{
					return true;
				}
			}

			return false;
		}
	}
}
