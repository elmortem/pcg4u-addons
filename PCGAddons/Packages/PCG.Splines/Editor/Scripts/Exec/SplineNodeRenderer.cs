using PCG.Editors;
using PCG.Splines.Tools;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	[CustomPcgNodeRenderer(typeof(SplineNodeExecutor))]
	public sealed class SplineNodeRenderer : PcgNodeRenderer
	{
		public override void DrawExtras()
		{
			var executor = (SplineNodeExecutor)Node;
			var key = SplineEditSessions.KeyFor(executor);
			var session = SplineEditSessions.Find(key);

			if (session != null && session.Container != null)
			{
				if (session.Executor != executor)
					SplineEditSessions.Rebind(key, executor);

				if (GUILayout.Button("Stop Edit"))
					SplineEditSessions.Stop(session);
			}
			else
			{
				if (GUILayout.Button("Start Edit"))
					StartEdit(executor);
			}
		}

		private void StartEdit(SplineNodeExecutor executor)
		{
			var graphId = executor.Graph != null ? executor.Graph.GraphId : string.Empty;
			var addressKey = executor.Address.ToKey();

			var marker = SplineEditSessions.FindOrphan(graphId, addressKey);
			SplineContainer container;

			if (marker != null && marker.Container != null)
			{
				container = marker.Container;
			}
			else
			{
				if (marker != null)
					Object.DestroyImmediate(marker.gameObject);

				var go = new GameObject("Spline Edit");
				container = go.AddComponent<SplineContainer>();
				marker = go.AddComponent<PcgSplineEditContainer>();
				marker.Container = container;
				marker.GraphId = graphId;
				marker.AddressKey = addressKey;
				executor.PopulateContainer(container);
			}

			executor.SetEditContainer(container);
			SplineEditSessions.Begin(executor, marker, container);
			Selection.activeGameObject = marker.gameObject;
			EditorApplication.delayCall += ActivateSplineDrawTool;
		}

		private static void ActivateSplineDrawTool()
		{
			ToolManager.SetActiveContext<SplineToolContext>();

			var drawTool = typeof(SplineToolContext).Assembly.GetType("UnityEditor.Splines.CreateSplineTool");
			if (drawTool != null)
				ToolManager.SetActiveTool(drawTool);
		}
	}
}
