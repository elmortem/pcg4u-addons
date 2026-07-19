using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines.Tools
{
	public static class SplineEditSessions
	{
		private static readonly Dictionary<string, SplineEditSession> _sessions = new();

		public static bool HasAny => _sessions.Count > 0;

		public static Dictionary<string, SplineEditSession>.ValueCollection All => _sessions.Values;

		public static string MakeKey(string graphId, string addressKey)
		{
			return graphId + "::" + addressKey;
		}

		public static string KeyFor(SplineNodeExecutor executor)
		{
			var graphId = executor.Graph != null ? executor.Graph.GraphId : string.Empty;
			return MakeKey(graphId, executor.Address.ToKey());
		}

		public static SplineEditSession Find(string key)
		{
			return _sessions.TryGetValue(key, out var session) ? session : null;
		}

		public static SplineEditSession Begin(SplineNodeExecutor executor, PcgSplineEditContainer marker, SplineContainer container)
		{
			var key = KeyFor(executor);
			var session = new SplineEditSession
			{
				Key = key,
				GraphId = marker.GraphId,
				AddressKey = marker.AddressKey,
				Marker = marker,
				Container = container,
				Executor = executor,
				Snapshot = SplineCopyUtility.CopySplines(executor.Data.Splines),
				SnapshotLinks = SplineCopyUtility.CopyLinks(executor.Data.Links),
				MatrixHash = container.transform.localToWorldMatrix.GetHashCode(),
				LinksHash = SplineHashUtility.LinksHash(container.KnotLinkCollection)
			};
			_sessions[key] = session;
			return session;
		}

		public static void Rebind(string key, SplineNodeExecutor executor)
		{
			if (!_sessions.TryGetValue(key, out var session))
				return;

			if (executor == null)
			{
				TerminateNoFlush(session, "PCG Splines: edit session lost its node, removing edit container.");
				return;
			}

			session.Executor = executor;
			executor.SetEditContainer(session.Container);
		}

		public static void Stop(SplineEditSession session)
		{
			if (session.Executor != null && session.Container != null)
				session.Executor.CommitStop(session.Container, session.Snapshot, session.SnapshotLinks);

			Terminate(session);
		}

		public static void FlushAndEnd(SplineEditSession session)
		{
			if (session.Executor != null && session.Container != null)
				session.Executor.WriteBack(session.Container, false);

			Terminate(session);
		}

		public static void HandleContainerDestroyed(SplineEditSession session)
		{
			if (session.Executor != null)
				session.Executor.SetEditContainer(null);

			session.Marker = null;
			session.Container = null;
			_sessions.Remove(session.Key);
		}

		public static void FlushAll(bool withUndo)
		{
			foreach (var session in _sessions.Values)
			{
				if (session.Executor != null && session.Container != null)
					session.Executor.WriteBack(session.Container, withUndo);
			}
		}

		public static void EndAllAndDestroy(bool flush)
		{
			foreach (var session in _sessions.Values)
			{
				if (flush && session.Executor != null && session.Container != null)
					session.Executor.WriteBack(session.Container, false);

				if (session.Executor != null)
					session.Executor.SetEditContainer(null);

				if (session.Marker != null)
					Object.DestroyImmediate(session.Marker.gameObject);

				session.Marker = null;
				session.Container = null;
			}

			_sessions.Clear();
		}

		public static PcgSplineEditContainer FindOrphan(string graphId, string addressKey)
		{
			var all = Object.FindObjectsByType<PcgSplineEditContainer>(FindObjectsSortMode.None);
			PcgSplineEditContainer chosen = null;
			var duplicates = new List<PcgSplineEditContainer>();

			foreach (var marker in all)
			{
				if (marker.GraphId != graphId || marker.AddressKey != addressKey)
					continue;

				if (chosen == null || marker.GetInstanceID() < chosen.GetInstanceID())
				{
					if (chosen != null)
						duplicates.Add(chosen);
					chosen = marker;
				}
				else
				{
					duplicates.Add(marker);
				}
			}

			foreach (var duplicate in duplicates)
			{
				Debug.LogWarning($"PCG Splines: multiple edit containers for the same node, removing duplicate '{duplicate.name}'.");
				Object.DestroyImmediate(duplicate.gameObject);
			}

			return chosen;
		}

		private static void Terminate(SplineEditSession session)
		{
			if (session.Executor != null)
				session.Executor.SetEditContainer(null);

			if (session.Marker != null)
				Object.DestroyImmediate(session.Marker.gameObject);

			session.Marker = null;
			session.Container = null;
			_sessions.Remove(session.Key);
		}

		private static void TerminateNoFlush(SplineEditSession session, string warning)
		{
			if (!string.IsNullOrEmpty(warning))
				Debug.LogWarning(warning);

			if (session.Marker != null)
				Object.DestroyImmediate(session.Marker.gameObject);

			session.Marker = null;
			session.Container = null;
			_sessions.Remove(session.Key);
		}
	}
}
