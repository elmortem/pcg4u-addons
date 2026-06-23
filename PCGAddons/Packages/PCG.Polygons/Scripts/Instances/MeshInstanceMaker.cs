using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Instances;
using PCG.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace PCG.Polygons
{
	public class MeshInstanceMaker : InstanceMakerBase
	{
		private GameObject AddMesh(MeshInstanceData data, Transform parent)
		{
			var go = new GameObject(string.IsNullOrEmpty(data.Name) ? "Mesh" : data.Name);
			go.transform.parent = parent;
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;

			var mesh = new Mesh();
			mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(data.Vertices);
			mesh.SetUVs(0, data.Uvs);
			mesh.SetTriangles(data.Triangles, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			var filter = go.AddComponent<MeshFilter>();
			filter.sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = data.Material;

			return go;
		}

		public override async UniTask<bool> TryAdd(string ownerKey, string groupName, IEnumerable<InstanceData> instances, CancellationToken ct = default)
		{
			if (instances == null)
				return true;

			var list = instances.ToList();
			if (!list.Any())
				return true;

			if (list.First() is not MeshInstanceData)
				return false;

			var meshes = list.Cast<MeshInstanceData>();

			using (var scope = OperationScope.Start(ownerKey))
			{
				foreach (var data in meshes)
				{
					var item = GetObjectsItem(ownerKey, groupName);
					var go = AddMesh(data, item.Parent);
					item.Objects.Add(go);

					await scope.Step(ct: ct);
				}
			}

			return true;
		}
	}
}
