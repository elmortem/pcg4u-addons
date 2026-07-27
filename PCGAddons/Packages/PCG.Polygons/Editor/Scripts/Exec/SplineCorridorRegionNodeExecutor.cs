using System.Collections.Generic;
using System.Threading;
using Clipper2ZLib;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Polygons.Utilities;
using PCG.Splines;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Polygons.City
{
	public sealed class SplineCorridorRegionNodeExecutor : PcgAsyncPreviewNodeExecutor<SplineCorridorRegionNode>
	{
		public PcgOutput<RegionSet> Result;

		public override bool IsEmpty => Result.Value == null || Result.Value.Count == 0;

		protected override UniTask DoComputeAsync(CancellationToken ct)
		{
			var openPaths = new List<float2[]>();
			var closedPaths = new List<float2[]>();
			var inputs = GetInputValues(nameof(Data.Splines), Data.Splines);
			float maxSegmentLength = math.max(0.1f, GetInputValue(nameof(Data.MaxSegmentLength), Data.MaxSegmentLength));
			float width = math.max(0.01f, GetInputValue(nameof(Data.Width), Data.Width));
			float planeY = 0f;
			bool hasPlane = false;

			if (inputs != null)
			{
				foreach (PcgSplineSet splines in inputs)
				{
					if (splines == null)
						continue;
					foreach (Spline spline in splines.Splines)
					{
						ct.ThrowIfCancellationRequested();
						float length = spline.GetLength();
						if (length <= 0.001f)
							continue;
						int segmentCount = math.max(1, Mathf.CeilToInt(length / maxSegmentLength));
						int count = spline.Closed ? segmentCount : segmentCount + 1;
						var path = new float2[count];
						for (int i = 0; i < count; i++)
						{
							float t = spline.Closed ? i / (float)segmentCount : i / (float)(count - 1);
							spline.Evaluate(t, out float3 position, out _, out _);
							path[i] = new float2(position.x, position.z);
							if (!hasPlane)
							{
								planeY = position.y;
								hasPlane = true;
							}
						}
						(spline.Closed ? closedPaths : openPaths).Add(path);
					}
				}
			}

			var regions = PolygonClipper.InflatePolylines(
				openPaths,
				closedPaths,
				width * 0.5f,
				ToJoinType(Data.Join),
				ToEndType(Data.Cap),
				2f);
			Result.Value = new RegionSet
			{
				PlaneY = planeY,
				Regions = regions
			};
			return UniTask.CompletedTask;
		}

		public override void DrawPreview(Transform transform)
		{
			var options = GetGizmosOptions();
			Gizmos.matrix = transform.localToWorldMatrix;
			RegionGizmoUtility.Draw(Result.Value, options.Color, new Color(options.Color.r, options.Color.g, options.Color.b, options.Color.a * 0.5f));
			Gizmos.matrix = Matrix4x4.identity;
		}

		private static JoinType ToJoinType(RoadJoinType join)
		{
			switch (join)
			{
				case RoadJoinType.Miter:
					return JoinType.Miter;
				case RoadJoinType.Square:
					return JoinType.Square;
				default:
					return JoinType.Round;
			}
		}

		private static EndType ToEndType(RoadCapType cap)
		{
			switch (cap)
			{
				case RoadCapType.Butt:
					return EndType.Butt;
				case RoadCapType.Square:
					return EndType.Square;
				default:
					return EndType.Round;
			}
		}
	}
}
