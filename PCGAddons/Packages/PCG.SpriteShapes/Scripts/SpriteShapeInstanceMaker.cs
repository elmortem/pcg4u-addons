using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;
using PCG.Instances;
using PCG.Utilities;

namespace PCG.SpriteShapes
{
	public class SpriteShapeInstanceMaker : InstanceMakerBase
	{
		public Vector3 GlobalRotation = new(90f, 0f, 0f);

		private GameObject AddSpriteShape(SpriteShapeInstanceData data, Transform parent)
		{
			var go = new GameObject(string.IsNullOrEmpty(data.Name) ? "SpriteShape" : data.Name);
			go.transform.parent = parent;
			go.transform.localPosition = Vector3.zero;
			go.transform.localScale = Vector3.one;
			go.transform.localRotation = Quaternion.Euler(GlobalRotation);
			
			var ssCon = go.AddComponent<SpriteShapeController>();
			ssCon.spriteShape = data.SpriteShape;

			Convert3DTo2D(data.Spline, data.Height, ssCon);
			
			ssCon.RefreshSpriteShape();
			ssCon.UpdateSpriteShapeParameters();

			return go;
		}

		private void Convert3DTo2D(UnityEngine.Splines.Spline spline, float height, SpriteShapeController controller)
		{
			var s = controller.spline;
			s.isOpenEnded = true;
			s.Clear();

			var minZ = float.MaxValue;
			var maxZ = float.MinValue;
			
			for (int i = 0; i < spline.Count; i++)
			{
				Vector3 pos = spline[i].Position;
				pos = pos.SwapYZ();
				minZ = Mathf.Min(minZ, pos.z);
				maxZ = Mathf.Max(maxZ, pos.z);
				s.InsertPointAt(i, pos);
				
				s.SetTangentMode(i, ShapeTangentMode.Continuous);
				s.SetCorner(i, true);
				s.SetHeight(i, height);

				if (i > 0 && i < spline.Count - 1)
				{
					Vector3 tangent = UnityEngine.Splines.SplineUtility.GetAutoSmoothTangent(spline[i - 1].Position, spline[i].Position,
						spline[i + 1].Position, UnityEngine.Splines.SplineUtility.CatmullRomTension);
					tangent = tangent.SwapYZ();
					s.SetRightTangent(i, tangent);
					s.SetLeftTangent(i, -tangent);
				}
			}

			var p = controller.transform.localPosition;
			p.y = (minZ + maxZ) * 0.5f;
			controller.transform.localPosition = p;
		}

		public override async UniTask<bool> TryAdd(int ownerNodeId, string groupName, IEnumerable<InstanceData> instances, CancellationToken ct = default)
		{
			if (instances == null)
			{
				return true;
			}
			
			var instanceDatas = instances.ToList();
			
			if (!instanceDatas.Any())
			{
				return true;
			}

			if (instanceDatas.First() is not SpriteShapeInstanceData)
			{
				return false;
			}
			
			var spriteShapes = instanceDatas.Cast<SpriteShapeInstanceData>();
			
			using (var scope = OperationScope.Start(ownerNodeId))
			{
				foreach (var data in spriteShapes)
				{
					var item = GetObjectsItem(ownerNodeId, groupName);
					var go = AddSpriteShape(data, item.Parent);
					item.Objects.Add(go);
					
					await scope.Step(ct: ct);
				}
			}

			return true;
		}
	}
}