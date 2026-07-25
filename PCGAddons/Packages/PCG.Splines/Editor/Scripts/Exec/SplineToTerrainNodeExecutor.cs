using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Splines.Tools;
using PCG.Splines.Utilities;
using PCG.Terrains;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class SplineToTerrainNodeExecutor : PcgAsyncPreviewNodeExecutor<SplineToTerrainNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			var terrain = GetInputValue(nameof(Data.Terrain), Data.Terrain);
			var terrainOffset = GetInputValue(nameof(Data.TerrainOffset), Data.TerrainOffset);
			float heightOffset = GetInputValue(nameof(Data.HeightOffset), Data.HeightOffset);
			float step = GetInputValue(nameof(Data.Step), Data.Step);

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length == 0)
			{
				Results.Value = new List<Spline>();
				return;
			}

			if (terrain == null)
			{
				var passthrough = new List<Spline>();
				foreach (var splines in splinesList)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline == null || spline.Count < 2)
							continue;

						passthrough.Add(spline);
					}
				}

				Results.Value = passthrough;
				return;
			}

			var resample = Data.Resample;
			var alignToTerrainNormal = Data.AlignToTerrainNormal;
			var copies = new List<Spline>();
			var positions = new List<float3[]>();
			float minX = float.MaxValue;
			float minZ = float.MaxValue;
			float maxX = float.MinValue;
			float maxZ = float.MinValue;

			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in splinesList)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline == null || spline.Count < 2)
							continue;

						Spline copy;
						if (resample)
							copy = await SplineResampleUtility.ResampleAsync(spline, step, scope, ct);
						else
							copy = SplineCopyUtility.CopySpline(spline);

						var knotPositions = new float3[copy.Count];
						for (int i = 0; i < copy.Count; i++)
						{
							float3 position = copy[i].Position;
							knotPositions[i] = position;
							minX = math.min(minX, position.x);
							maxX = math.max(maxX, position.x);
							minZ = math.min(minZ, position.z);
							maxZ = math.max(maxZ, position.z);
						}

						copies.Add(copy);
						positions.Add(knotPositions);
						await scope.Step(ct: ct);
					}
				}
			}

			if (copies.Count == 0)
			{
				Results.Value = new List<Spline>();
				return;
			}

			var window = SplineTerrainWindow.Capture(terrain, terrainOffset, minX, maxX, minZ, maxZ);
			var heights = new float[copies.Count][];
			var normals = new float3[copies.Count][];
			var inBounds = new bool[copies.Count][];
			bool outOfBounds = false;

			await PcgWorkerScheduler.RunAsync(() =>
			{
				int counter = 0;
				for (int s = 0; s < positions.Count; s++)
				{
					var knotPositions = positions[s];
					var splineHeights = new float[knotPositions.Length];
					var splineNormals = new float3[knotPositions.Length];
					var splineInBounds = new bool[knotPositions.Length];

					for (int i = 0; i < knotPositions.Length; i++)
					{
						float3 position = knotPositions[i];
						if (window.TrySample(position.x, position.z, out float height, out float3 normal))
						{
							splineHeights[i] = height + heightOffset;
							splineNormals[i] = normal;
							splineInBounds[i] = true;
						}
						else
						{
							splineHeights[i] = position.y;
							splineNormals[i] = math.up();
							outOfBounds = true;
						}

						counter++;
						if (counter % 1024 == 0)
						{
							ct.ThrowIfCancellationRequested();
							PcgComputeSystem.ReportProgress(this);
						}
					}

					heights[s] = splineHeights;
					normals[s] = splineNormals;
					inBounds[s] = splineInBounds;
				}
			}, ct);

			var results = new List<Spline>(copies.Count);
			using (var scope = OperationScope.Start(this))
			{
				for (int s = 0; s < copies.Count; s++)
				{
					var copy = copies[s];
					for (int i = 0; i < copy.Count; i++)
					{
						if (!inBounds[s][i])
							continue;

						var knot = copy[i];
						knot.Position.y = heights[s][i];
						copy.SetKnot(i, knot);
						await scope.Step(ct: ct);
					}

					if (alignToTerrainNormal)
					{
						for (int i = 0; i < copy.Count; i++)
						{
							if (!inBounds[s][i])
								continue;

							AlignKnot(copy, i, normals[s][i]);
							await scope.Step(ct: ct);
						}
					}

					results.Add(copy);
				}
			}

			Results.Value = results;
			if (outOfBounds)
				Debug.LogWarning("[Spline To Terrain] Part of the splines is outside the terrain and keeps the spline height and up vector.");
		}

		private static void AlignKnot(Spline spline, int index, float3 terrainNormal)
		{
			var knot = spline[index];
			float3 worldIn = math.rotate(knot.Rotation, knot.TangentIn);
			float3 worldOut = math.rotate(knot.Rotation, knot.TangentOut);
			float t = SplineUtility.ConvertIndexUnit(spline, index, PathIndexUnit.Knot, PathIndexUnit.Normalized);
			float3 tangent = math.normalizesafe(spline.EvaluateTangent(t));

			if (math.lengthsq(tangent) < 1e-8f)
				tangent = math.normalizesafe(worldOut - worldIn, math.forward());

			float3 up = terrainNormal - tangent * math.dot(terrainNormal, tangent);
			if (math.lengthsq(up) < 1e-8f)
			{
				float3 originalUp = math.rotate(knot.Rotation, math.up());
				up = originalUp - tangent * math.dot(originalUp, tangent);
			}

			up = math.normalizesafe(up, math.up());
			quaternion rotation = quaternion.LookRotationSafe(tangent, up);
			quaternion inverse = math.inverse(rotation);
			knot.TangentIn = math.rotate(inverse, worldIn);
			knot.TangentOut = math.rotate(inverse, worldOut);
			knot.Rotation = rotation;
			spline.SetKnot(index, knot);
		}

		public override int GetVersionSalt()
		{
			unchecked
			{
				int hash = 17;
				var terrain = GetInputValue(nameof(Data.Terrain), Data.Terrain);
				if (terrain != null)
					hash = (hash * 397) ^ PcgTerrainContentVersion.Get(terrain);
				return hash;
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
		}
	}
}
