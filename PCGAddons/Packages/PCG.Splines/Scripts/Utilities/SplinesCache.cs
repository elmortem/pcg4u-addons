#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines.Utilities
{
    public static class SplinesCache
	{
        private struct SplineKey
        {
            public Spline Spline;
            public int Segments;
        }

        private static readonly Dictionary<SplineKey, Vector3[]> CachedPositions = new();

		[InitializeOnLoadMethod]
		static void Initialize()
		{
			Spline.Changed -= OnSplineChanged;
			Spline.Changed += OnSplineChanged;
			Undo.undoRedoPerformed -= ClearAllCache;
			Undo.undoRedoPerformed += ClearAllCache;
			PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
			PrefabStage.prefabStageClosing += OnPrefabStageClosing;
			PrefabUtility.prefabInstanceReverting -= OnPrefabInstanceReverting;
			PrefabUtility.prefabInstanceReverting += OnPrefabInstanceReverting;
		}

        public static void GetCachedPositions(Spline spline, int segments, out Vector3[] positions)
		{
			if (spline == null)
			{
				positions = null;
				return;
			}
			
			int count = spline.Closed ? spline.Count : spline.Count - 1;
			if (segments <= 1)
				segments = 32;
			
            var key = new SplineKey { Spline = spline, Segments = segments };
            if (!CachedPositions.TryGetValue(key, out positions))
			{
				positions = new Vector3[count * segments];
				
				float inv = 1f / (segments - 1);
				for(int i = 0; i < count; ++i)
				{
					var curve = spline.GetCurve(i);
					var startIndex = i * segments;
					for(int n = 0; n < segments; n++)
						positions[startIndex + n] = CurveUtility.EvaluatePosition(curve, n * inv);
				}

                CachedPositions[key] = positions;
			}
		}
        
		public static void ClearSplineCache(Spline spline)
		{
			var keys = new List<SplineKey>();
			foreach (var pair in CachedPositions)
			{
				if (pair.Key.Spline == spline)
					keys.Add(pair.Key);
			}
			foreach (var key in keys)
				CachedPositions.Remove(key);
		}
		
		public static void ClearAllCache()
		{
			CachedPositions.Clear();
		}
		
        private static void OnSplineChanged(Spline spline, int index, SplineModification modification)
        {
	        ClearSplineCache(spline);
        }

        private static void OnPrefabStageClosing(PrefabStage _)
        {
	        ClearAllCache();
        }

        private static void OnPrefabInstanceReverting(GameObject _)
        {
	        ClearAllCache();
        }
	}
}
#endif