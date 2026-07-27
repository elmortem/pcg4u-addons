using System.Collections.Generic;
using PCG.Exec;
using PCG.Splines.Tools;
using PCG.Splines.Utilities;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public sealed class GameObjectsToSplinesAdapter : PcgPortAdapter<List<GameObject>, PcgSplineSet>
	{
		private readonly Dictionary<int, PcgSplineSet> _issued = new();

		protected override PcgSplineSet Convert(List<GameObject> value, PcgNodeExecutor consumer)
		{
			var consumerId = consumer.Node.NodeId;
			if (_issued.TryGetValue(consumerId, out var previous))
			{
				foreach (var spline in previous.Splines)
				{
					SplinesCache.ClearSplineCache(spline);
				}
			}

			var results = new PcgSplineSet();

			foreach (var go in value)
			{
				if (go == null)
					continue;

				foreach (var container in go.GetComponentsInChildren<SplineContainer>())
				{
					foreach (var spline in container.Splines)
					{
						results.Add(SplineCopyUtility.CopySpline(spline, container.transform.localToWorldMatrix));
					}
				}
			}

			_issued[consumerId] = results;
			return results;
		}
	}
}
