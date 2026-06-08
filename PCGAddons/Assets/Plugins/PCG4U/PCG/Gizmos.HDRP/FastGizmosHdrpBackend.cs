using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PCG.Fast.Hdrp
{
	public sealed class FastGizmosHdrpBackend : IFastGizmosRenderBackend
	{
		private const string VolumeHostName = "PcgFastGizmosHdrpVolume";

		private FastGizmosBackendContext _context;
		private Material _material;
		private GameObject _volumeHost;

		public void Initialize(FastGizmosBackendContext context)
		{
			_context = context;
			_material = new Material(Shader.Find("PCG4U/FastGizmosShapeHdrp"));
			_material.SetVector("_LightDirection", new Vector4(0.1f, -0.7f, 0.3f, 0));
			_material.SetFloat("_ShadowStrength", 0.6f);

			DestroyStaleVolumes();

			_volumeHost = new GameObject(VolumeHostName);
			_volumeHost.hideFlags = HideFlags.HideAndDontSave;
			var volume = _volumeHost.AddComponent<CustomPassVolume>();
			volume.isGlobal = true;
			volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
			volume.AddPassOfType(typeof(FastGizmosHdrpPass));

			FastGizmosHdrpPass.Backend = this;
			AssemblyReloadEvents.beforeAssemblyReload += DestroyVolume;
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
			AssemblyReloadEvents.beforeAssemblyReload -= DestroyVolume;
			FastGizmosHdrpPass.Backend = null;
			DestroyVolume();
			if (_material != null)
			{
				Object.DestroyImmediate(_material);
			}
		}

		internal void Draw(CustomPassContext ctx)
		{
			if (ctx.hdCamera.camera != _context.GetSceneCamera())
			{
				return;
			}

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

					ctx.cmd.DrawMeshInstancedIndirect(mesh, 0, _material, 0, branch.ArgsBuffer, 0, branch.PropertyBlock);
				}
			}
		}

		private void DestroyVolume()
		{
			if (_volumeHost != null)
			{
				Object.DestroyImmediate(_volumeHost);
				_volumeHost = null;
			}
		}

		private void DestroyStaleVolumes()
		{
			var volumes = Resources.FindObjectsOfTypeAll<CustomPassVolume>();
			foreach (var volume in volumes)
			{
				if (volume.gameObject.name == VolumeHostName)
				{
					Object.DestroyImmediate(volume.gameObject);
				}
			}
		}
	}
}
