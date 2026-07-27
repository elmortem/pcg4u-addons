using System;
using System.Collections.Generic;
using PCG.Splines.Utilities;
using PCG.Values;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	[Serializable]
	[PcgValueMenuPath("Splines/Splines")]
	public sealed class SplinesValue : PcgValue
	{
		public List<SplineContainer> Containers = new();

		public override Type ValueType => typeof(PcgSplineSet);

		public override bool IsArray => true;

		public override object GetValue(Transform transform)
		{
			var result = new List<Spline>();

			foreach (var source in Containers)
			{
				if (source == null)
					continue;

				foreach (var spline in source.Splines)
				{
					var transformedSpline = new Spline();
					transformedSpline.Closed = spline.Closed;
					for (var i = 0; i < spline.Count; ++i)
					{
						var knot = spline[i];
						var transformedKnot = new BezierKnot(
							source.transform.TransformPoint(knot.Position),
							source.transform.TransformDirection(knot.TangentIn),
							source.transform.TransformDirection(knot.TangentOut),
							source.transform.rotation * knot.Rotation
						);
						transformedSpline.Add(transformedKnot, spline.GetTangentMode(i));
					}

					SplineWidthUtility.Copy(spline, transformedSpline);
					result.Add(transformedSpline);
				}
			}

			return new PcgSplineSet(result);
		}

		public override int GetContentHash()
		{
			unchecked
			{
				int hash = Containers.Count;
				for (int i = 0; i < Containers.Count; i++)
				{
					var container = Containers[i];
					hash = (hash * 397) ^ (container != null ? container.GetInstanceID() : 0);
					if (container != null)
					{
						hash = (hash * 397) ^ container.transform.localToWorldMatrix.GetHashCode();
						foreach (var spline in container.Splines)
						{
							hash = (hash * 397) ^ SplinesUtility.GetContentHash(spline);
						}
					}
				}

				return hash;
			}
		}
	}
}
