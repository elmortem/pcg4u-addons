using System.Collections.Generic;
using PCG.Exec;
using PCG.Splines.Utilities;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public sealed class GameObjectsToSplinesAdapter : PcgPortAdapter<List<GameObject>, List<Spline>>
	{
		private readonly Dictionary<int, List<Spline>> _issued = new();

		protected override List<Spline> Convert(List<GameObject> value, PcgNodeExecutor consumer)
		{
			var consumerId = consumer.Node.NodeId;
			if (_issued.TryGetValue(consumerId, out var previous))
			{
				foreach (var spline in previous)
				{
					SplinesCache.ClearSplineCache(spline);
				}
			}

			var results = new List<Spline>();

			foreach (var go in value)
			{
				if (go == null)
					continue;

				foreach (var container in go.GetComponentsInChildren<SplineContainer>())
				{
					foreach (var spline in container.Splines)
					{
						var transformed = new Spline();
						transformed.Closed = spline.Closed;
						for (var i = 0; i < spline.Count; ++i)
						{
							var knot = spline[i];
							transformed.Add(new BezierKnot(
								container.transform.TransformPoint(knot.Position),
								container.transform.TransformDirection(knot.TangentIn),
								container.transform.TransformDirection(knot.TangentOut),
								container.transform.rotation * knot.Rotation
							), spline.GetTangentMode(i));
						}

						results.Add(transformed);
					}
				}
			}

			_issued[consumerId] = results;
			return results;
		}
	}
}
