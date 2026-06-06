using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BRG;
using Cysharp.Threading.Tasks;
using PCG.Utilities;
using UnityEngine;
using PCG.Instances;

namespace PCG.BRG
{
	public class BrgInstanceMaker : InstanceMakerBase
	{
		public ComputeShader MemcpyShader;

		public override void Begin()
		{
		}
		
		public override async UniTask<bool> TryAdd(int ownerNodeId, string groupName, IEnumerable<InstanceData> instances, CancellationToken ct = default)
		{
			if (instances == null)
			{
				return true;
			}

			var instanceDatas = instances.ToList();
			if (!instanceDatas.Any(p => p is BrgInstanceData))
			{
				return false;
			}
			
			using (var scope = OperationScope.Start(ownerNodeId))
			{
				foreach (var data in instanceDatas)
				{
					if (data is BrgInstanceData brgData)
					{
						var prefab = brgData.Prefab;
						if (prefab == null)
						{
							Debug.LogError($"Prefab is empty.");
							continue;
						}
						var meshFilter = prefab.GetComponentInChildren<MeshFilter>();
						var meshRenderer = prefab.GetComponentInChildren<MeshRenderer>();
						var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
						var material = meshRenderer != null ? meshRenderer.sharedMaterial : null;

						if (mesh == null)
						{
							Debug.LogError($"Mesh at '{prefab.name}' not found.");
							continue;
						}

						if (material == null)
						{
							Debug.LogError($"Material at '{prefab.name}' not found.");
							continue;
						}

						var count = Mathf.CeilToInt(brgData.Points.Count / 65000f);
						for (int i = 0; i < count; i++)
						{
							var item = GetObjectsItem(ownerNodeId, groupName);
							var go = new GameObject($"RBG_{prefab.name}_{i}");
							go.transform.parent = item.Parent;
							item.Objects.Add(go);
							
							var brg = go.AddComponent<BrgContainer>();

							var items = new List<BrgItem>();
							var countMax = Mathf.Min((i + 1) * 65000, brgData.Points.Count);
							for (int j = i * 65000; j < countMax; j++)
							{
								var point = brgData.Points[j];
								var pos = point.Position;
								if (Parent != transform)
								{
									pos = transform.TransformPoint(pos);
									pos = Parent.InverseTransformPoint(pos);
								}
								
								var normal = point.Normal;
								var angleY = point.Angle;
								var rotation = Quaternion.FromToRotation(Vector3.up, normal);
								var yRotation = Quaternion.Euler(0, angleY, 0);
								rotation *= yRotation;
								var rot = rotation.eulerAngles;
								
								var scale = point.Scale;

								items.Add(new BrgItem
								{
									Position = pos,
									Rotation = rot,
									Scale = new Vector3(scale, scale, scale),
									Color = new Color(1f, 1f, 1f, 1f)
								});
								
								await scope.Step(ct: ct);
							}

							brg.Init(mesh, material, MemcpyShader, items);
						}
					}
				}
			}

			return true;
		}
	}
}