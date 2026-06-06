using System.Collections.Generic;
using PCG.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	[CustomPcgNodeRenderer(typeof(SplineNodeExecutor))]
	public sealed class SplineNodeRenderer : PcgNodeRenderer
	{
		private GameObject _editContainerObject;

		public override void DrawExtras()
		{
			var executor = (SplineNodeExecutor)Node;

			if (_editContainerObject == null)
			{
				if (GUILayout.Button("Start Edit"))
					StartEdit(executor);
			}
			else
			{
				if (GUILayout.Button("Stop Edit"))
					StopEdit(executor);
			}
		}

		private void StartEdit(SplineNodeExecutor executor)
		{
			_editContainerObject = new GameObject("Spline Edit");
			_editContainerObject.AddComponent<SplineContainer>();
			executor.SetEditContainer(_editContainerObject.transform);
			Selection.activeGameObject = _editContainerObject;
		}

		private void StopEdit(SplineNodeExecutor executor)
		{
			if (_editContainerObject != null)
			{
				var container = _editContainerObject.GetComponent<SplineContainer>();
				if (container != null)
				{
					var splines = new List<Spline>(container.Splines);
					executor.SetData(splines);
				}

				Object.DestroyImmediate(_editContainerObject);
				_editContainerObject = null;
			}

			executor.SetEditContainer(null);
		}
	}
}
