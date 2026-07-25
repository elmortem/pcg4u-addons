using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
using PCG.Splines.Tools;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class SplineIntersectionNodeExecutor : PcgAsyncPreviewNodeExecutor<SplineIntersectionNode>, INodeInfo, IPointsCount
	{
		public PcgOutput<SplineNetworkTopology> Topology;
		public PcgOutput<List<PointData>> Results;
		public PcgOutput<List<Spline>> SnappedSplines;

		public override bool IsEmpty => Results.Value == null || Results.Value.Count == 0;
		public int PointsCount => Results.Value?.Count ?? 0;
		public bool HasNodeInfo => Topology.Value != null && (IsComputed || IsComputing);
		public string NodeInfo => $"Junctions: {Topology.Value?.Junctions.Count ?? 0}, Cuts: {Topology.Value?.Cuts.Count ?? 0}";

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			var flat = SplineNetworkInput.Flatten(splinesList);
			if (flat.Count == 0)
			{
				Topology.Value = new SplineNetworkTopology();
				Results.Rent(0);
				SnappedSplines.Value = new List<Spline>();
				return;
			}

			var tolerance = GetInputValue(nameof(Data.IntersectionTolerance), Data.IntersectionTolerance);
			var mergeDistance = GetInputValue(nameof(Data.MergeDistance), Data.MergeDistance);
			var maxHeight = GetInputValue(nameof(Data.MaxHeightDifference), Data.MaxHeightDifference);
			var endpointSnapDistance = math.max(0f, GetInputValue(nameof(Data.EndpointSnapDistance), Data.EndpointSnapDistance));
			var snapped = endpointSnapDistance > 0f
				? await SnapEndpointsAsync(flat, endpointSnapDistance, maxHeight, ct)
				: flat;
			SnappedSplines.Value = new List<Spline>(snapped);

			var snapshots = new SplineSnapshot[snapped.Count];
			using (var scope = OperationScope.Start(this))
			{
				for (int i = 0; i < snapped.Count; i++)
				{
					var spline = snapped[i];
					if (spline != null && spline.Count >= 2)
						snapshots[i] = SplineSnapshot.Capture(spline);

					await scope.Step(ct: ct);
				}
			}

			var solved = await PcgWorkerScheduler.RunAsync(
				() => SplineIntersectionSolver.Solve(snapshots, tolerance, mergeDistance, maxHeight, ct,
					() => PcgComputeSystem.ReportProgress(this)),
				ct);

			if (solved.ToleranceNotReached)
				Debug.LogWarning($"[Spline Intersection] Intersection tolerance {tolerance} was not reached on some high-curvature curves.");
			if (solved.CollinearOverlap)
				Debug.LogWarning("[Spline Intersection] Collinear overlap longer than merge distance detected; it does not form a junction.");

			Topology.Value = solved.Topology;

			var junctions = solved.Topology.Junctions;
			Results.Rent(junctions.Count);
			for (int i = 0; i < junctions.Count; i++)
			{
				Results.Value.Add(new PointData
				{
					Position = junctions[i].Position,
					Normal = Vector3.up,
					Angle = 0f,
					Scale = 1f,
					Density = 1f
				});
			}
		}

		private async UniTask<List<Spline>> SnapEndpointsAsync(
			List<Spline> splines, float snapDistance, float maxHeightDifference, CancellationToken ct)
		{
			var results = new List<Spline>(splines.Count);
			using var scope = OperationScope.Start(this);
			for (int i = 0; i < splines.Count; i++)
			{
				var spline = splines[i];
				results.Add(spline != null ? SplineCopyUtility.CopySpline(spline) : null);
				await scope.Step(ct: ct);
			}

			float snapSq = snapDistance * snapDistance;
			for (int i = 0; i < splines.Count; i++)
			{
				var source = splines[i];
				if (source == null || source.Closed || source.Count < 2)
					continue;

				await SnapEndpointAsync(splines, results[i], i, 0, snapSq, maxHeightDifference, scope, ct);
				await SnapEndpointAsync(splines, results[i], i, source.Count - 1, snapSq, maxHeightDifference, scope, ct);
			}

			return results;
		}

		private static async UniTask SnapEndpointAsync(
			List<Spline> sources,
			Spline result,
			int sourceIndex,
			int knotIndex,
			float snapSq,
			float maxHeightDifference,
			OperationScope scope,
			CancellationToken ct)
		{
			float3 point = sources[sourceIndex][knotIndex].Position;
			float bestSq = snapSq;
			float3 best = point;
			bool found = false;

			for (int i = 0; i < sources.Count; i++)
			{
				if (i == sourceIndex)
					continue;

				var candidate = sources[i];
				if (candidate == null || candidate.Count < 2)
					continue;

				SplineUtility.GetNearestPoint(candidate, point, out float3 nearest, out float t, 8, 3);
				await scope.Step(ct: ct);
				if (t <= 0.001f || t >= 0.999f)
					continue;

				if (maxHeightDifference > 0f && math.abs(nearest.y - point.y) > maxHeightDifference)
					continue;

				float distSq = math.distancesq(point, nearest);
				if (distSq >= bestSq)
					continue;

				bestSq = distSq;
				best = nearest;
				found = true;
			}

			if (!found)
				return;

			var knot = result[knotIndex];
			knot.Position = best;
			result.SetKnot(knotIndex, knot);
		}

		public override void DrawPreview(Transform transform)
		{
			if (Results.Value == null)
				return;

			var gizmosOptions = GetGizmosOptions();
			Gizmos.color = gizmosOptions.Color;
			GizmosUtility.DrawPoints(Results.Value, gizmosOptions, transform);

			if (Topology.Value == null)
				return;

			var junctions = Topology.Value.Junctions;
			var color = Gizmos.color;
			Gizmos.matrix = transform.localToWorldMatrix;
			for (int i = 0; i < junctions.Count; i++)
			{
				var junction = junctions[i];
				Gizmos.color = ValencyColor(junction.Valency, gizmosOptions.Color);
				Gizmos.DrawWireSphere(junction.Position, ValencyRadius(junction.Valency));
			}
			Gizmos.matrix = Matrix4x4.identity;
			Gizmos.color = color;
		}

		private static Color ValencyColor(int valency, Color baseColor)
		{
			if (valency <= 2)
				return baseColor;
			if (valency == 3)
				return Color.yellow;
			return Color.red;
		}

		private static float ValencyRadius(int valency)
		{
			return 0.2f * math.max(1, valency - 1);
		}
	}
}
