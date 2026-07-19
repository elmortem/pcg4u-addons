using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace PCG.Splines.Tools
{
	public static class SplineNodeEditWatcher
	{
		private static readonly List<SplineEditSession> _buffer = new();
		private static double _nextTick;

		[InitializeOnLoadMethod]
		private static void Initialize()
		{
			Spline.Changed -= OnSplineChanged;
			Spline.Changed += OnSplineChanged;

			SplineContainer.SplineAdded -= OnSplineAdded;
			SplineContainer.SplineAdded += OnSplineAdded;
			SplineContainer.SplineRemoved -= OnSplineRemoved;
			SplineContainer.SplineRemoved += OnSplineRemoved;
			SplineContainer.SplineReordered -= OnSplineReordered;
			SplineContainer.SplineReordered += OnSplineReordered;

			Undo.undoRedoPerformed -= OnUndoRedo;
			Undo.undoRedoPerformed += OnUndoRedo;

			AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

			EditorSceneManager.sceneClosing -= OnSceneClosing;
			EditorSceneManager.sceneClosing += OnSceneClosing;

			EditorApplication.update -= OnUpdate;
			EditorApplication.update += OnUpdate;
		}

		private static void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
		{
			if (!SplineEditSessions.HasAny)
				return;

			foreach (var session in SplineEditSessions.All)
			{
				if (session.Container == null)
					continue;

				if (ContainsSpline(session.Container, spline))
				{
					session.Dirty = true;
					return;
				}
			}
		}

		private static void OnSplineAdded(SplineContainer container, int index)
		{
			MarkContainerDirty(container);
		}

		private static void OnSplineRemoved(SplineContainer container, int index)
		{
			MarkContainerDirty(container);
		}

		private static void OnSplineReordered(SplineContainer container, int previousIndex, int newIndex)
		{
			MarkContainerDirty(container);
		}

		private static void MarkContainerDirty(SplineContainer container)
		{
			if (!SplineEditSessions.HasAny)
				return;

			foreach (var session in SplineEditSessions.All)
			{
				if (session.Container == container)
				{
					session.Dirty = true;
					return;
				}
			}
		}

		private static void OnUndoRedo()
		{
			if (!SplineEditSessions.HasAny)
				return;

			foreach (var session in SplineEditSessions.All)
				session.Dirty = true;
		}

		private static void OnBeforeAssemblyReload()
		{
			SplineEditSessions.FlushAll(false);
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingEditMode)
				SplineEditSessions.EndAllAndDestroy(true);
		}

		private static void OnSceneClosing(Scene scene, bool removingScene)
		{
			if (!SplineEditSessions.HasAny)
				return;

			_buffer.Clear();
			foreach (var session in SplineEditSessions.All)
				_buffer.Add(session);

			foreach (var session in _buffer)
			{
				if (session.Marker == null)
					continue;

				if (session.Marker.gameObject.scene != scene)
					continue;

				SplineEditSessions.FlushAndEnd(session);
			}
		}

		private static void OnUpdate()
		{
			if (!SplineEditSessions.HasAny)
				return;

			double now = EditorApplication.timeSinceStartup;
			if (now < _nextTick)
				return;
			_nextTick = now + 0.25;

			_buffer.Clear();
			foreach (var session in SplineEditSessions.All)
				_buffer.Add(session);

			for (int i = 0; i < _buffer.Count; i++)
			{
				var session = _buffer[i];
				if (session.Container == null)
				{
					SplineEditSessions.HandleContainerDestroyed(session);
					continue;
				}

				int matrixHash = session.Container.transform.localToWorldMatrix.GetHashCode();
				int linksHash = SplineHashUtility.LinksHash(session.Container.KnotLinkCollection);
				if (matrixHash != session.MatrixHash || linksHash != session.LinksHash)
				{
					session.MatrixHash = matrixHash;
					session.LinksHash = linksHash;
					session.Dirty = true;
				}

				if (session.Dirty)
				{
					session.Dirty = false;
					if (session.Executor != null)
						session.Executor.WriteBack(session.Container, false);
				}
			}
		}

		private static bool ContainsSpline(SplineContainer container, Spline spline)
		{
			var splines = container.Splines;
			for (int i = 0; i < splines.Count; i++)
			{
				if (splines[i] == spline)
					return true;
			}

			return false;
		}
	}
}
