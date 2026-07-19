using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Points;
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
				return;
			}

			var tolerance = GetInputValue(nameof(Data.IntersectionTolerance), Data.IntersectionTolerance);
			var mergeDistance = GetInputValue(nameof(Data.MergeDistance), Data.MergeDistance);
			var maxHeight = GetInputValue(nameof(Data.MaxHeightDifference), Data.MaxHeightDifference);

			var snapshots = new SplineSnapshot[flat.Count];
			using (var scope = OperationScope.Start(this))
			{
				for (int i = 0; i < flat.Count; i++)
				{
					var spline = flat[i];
					if (spline != null && spline.Count >= 2)
						snapshots[i] = SplineSnapshot.Capture(spline);

					await scope.Step(ct: ct);
				}
			}

			SplineIntersectionResult solved;
			await UniTask.SwitchToThreadPool();
			try
			{
				solved = SplineIntersectionSolver.Solve(snapshots, tolerance, mergeDistance, maxHeight, ct,
					() => PcgComputeSystem.ReportProgress(this));
			}
			finally
			{
				await UniTaskEditor.SwitchToEditorThread();
			}

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
