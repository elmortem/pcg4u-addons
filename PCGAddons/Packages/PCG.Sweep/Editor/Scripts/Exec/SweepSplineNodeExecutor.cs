using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Instances;
using PCG.Splines;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	public class SweepSplineNodeExecutor : PcgAsyncPreviewNodeExecutor<SweepSplineNode>, INodeInfo, IInstancesNode
	{
		private const int MaxVerticesPerMesh = 2_000_000;
		private const int LutSize = 256;

		public PcgOutput<List<MeshInstanceData>> Results;

		private Vector3[] _previewBlackPoints;
		private Vector3[][] _previewCutChords;
		private Vector3[][] _previewFreeSplines;
		private Vector3[][] _previewDebugCuts;
		private int[] _previewDebugState;

		private IInstanceMakerContainer InstanceMakerContainer => Graph?.Host as IInstanceMakerContainer;

		public override bool IsEmpty => Results.Value == null || Results.Value.Count == 0;

		public override bool IsPreview
		{
			get
			{
				if (Data.Enabled && Data.MergeIntersections && Data.ShowIntersections && _previewFreeSplines != null)
					return IsPreviewLocal || IsPreviewGlobal;

				return !IsEmpty && (IsPreviewLocal || IsPreviewGlobal);
			}
		}

		public bool HasNodeInfo => !IsEmpty && (IsComputed || IsComputing);
		public string NodeInfo => $"Meshes: {Results.Value.Count}, Triangles: {TriangleCount()}";

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			bool mergeMode = Data.Enabled && Data.MergeIntersections;

			if (mergeMode)
				await ComputeMergedAsync(ct);
			else
				await ComputeSingleAsync(ct);
		}

		private async UniTask ComputeMergedAsync(CancellationToken ct)
		{
			_previewBlackPoints = null;
			_previewCutChords = null;
			_previewFreeSplines = null;
			_previewDebugCuts = null;
			_previewDebugState = null;

			SweepSnapshot snapshot = null;
			var splines = new List<Spline>();
			SweepRibbonPath[] fullPaths = null;
			float step = 0f;
			float thickness = 0f;
			float extrudeHeight = 0f;

			using (var scope = OperationScope.Start(this))
			{
				snapshot = await BuildSnapshotAsync(scope, ct, splines);
				step = math.max(0.05f, GetInputValue(nameof(Data.Step), Data.Step));
				thickness = math.max(0f, GetInputValue(nameof(Data.MergeThickness), Data.MergeThickness));

				extrudeHeight = ConfigureRectangleRibbon(snapshot);

				await scope.Step(ct: ct);
				if (snapshot != null && splines.Count == snapshot.Frames.Length)
				{
					fullPaths = new SweepRibbonPath[splines.Count];
					for (int i = 0; i < splines.Count; i++)
					{
						float length = splines[i].GetLength();
						fullPaths[i] = await SweepRibbonSampling.CaptureAsync(splines[i], 0f, length, step, 2, scope, ct);
					}
				}
			}

			if (snapshot == null || snapshot.Frames.Length == 0 || splines.Count != snapshot.Frames.Length || fullPaths == null)
			{
				Results.Value = new List<MeshInstanceData>();
				await SyncSceneAsync(Results.Value, ct);
				return;
			}

			if (!SweepRibbonSplitter.CanBuild(snapshot, out string profileFailure))
			{
				await ComputeSingleAsync(ct);
				return;
			}

			Action reportProgress = () => PcgComputeSystem.ReportProgress(this);
			var split = await PcgWorkerScheduler.RunAsync(
				() => SweepRibbonSplitter.Split(snapshot, fullPaths, thickness, ct, reportProgress),
				ct);

			_previewBlackPoints = split.BlackPoints.ToArray();
			_previewCutChords = split.CutChords.ToArray();
			_previewFreeSplines = split.FreeSplines.ToArray();
			_previewDebugCuts = split.DebugCuts.ToArray();
			_previewDebugState = split.DebugState.ToArray();

			var material = GetInputValue(nameof(Data.Material), Data.Material);
			var junctionMaterial = GetInputValue(nameof(Data.JunctionMaterial), Data.JunctionMaterial);
			if (junctionMaterial == null)
				junctionMaterial = material;
			float maxStep = math.max(step, GetInputValue(nameof(Data.MaxStep), Data.MaxStep));
			float maxAngleRad = math.radians(math.clamp(GetInputValue(nameof(Data.MaxAngle), Data.MaxAngle), 0.5f, 180f));
			int vpr = snapshot.ProfilePoints.Length;

			var greenSnaps = new List<SweepSnapshot>();
			var bluePaths = new List<SweepRibbonPath>();
			var blueFallback = new List<SweepSnapshot>();
			var blueSplineIndices = new List<int>();
			var piecePaths = new SweepRibbonPath[split.Pieces.Count];
			using (var scope = OperationScope.Start(this))
			{
				for (int p = 0; p < split.Pieces.Count; p++)
				{
					var piece = split.Pieces[p];
					if (piece.State == SweepRibbonPiece.Green)
					{
						var pieceSnap = await BuildPieceSnapshotAsync(piece, splines, snapshot, step, maxStep, maxAngleRad, vpr, scope, ct);
						if (pieceSnap != null)
							greenSnaps.Add(pieceSnap);
					}
					else
					{
						int minSamples = piece.State == SweepRibbonPiece.Blue ? 8 : 2;
						piecePaths[p] = await SweepRibbonSampling.CaptureAsync(
							splines[piece.Spline],
							piece.StartStation,
							piece.EndStation,
							step,
							minSamples,
							scope,
							ct);
						if (piece.State == SweepRibbonPiece.Blue)
						{
							bluePaths.Add(piecePaths[p]);
							blueFallback.Add(await BuildPieceSnapshotAsync(piece, splines, snapshot, step, maxStep, maxAngleRad, vpr, scope, ct));
							blueSplineIndices.Add(piece.Spline);
						}
					}
					await scope.Step(ct: ct);
				}
			}

			var greenMeshes = new SweepMeshData[greenSnaps.Count];
			var blueMeshes = new SweepMeshData[bluePaths.Count];
			List<SweepMeshData> patchMeshes = null;
			int patchIndex = greenMeshes.Length + blueMeshes.Length;
			await PcgWorkerScheduler.RunIndexedAsync(patchIndex + 1, index =>
			{
				if (index < greenMeshes.Length)
				{
					var mesh = SweepMeshBuilder.Build(greenSnaps[index], 0, ct, reportProgress);
					greenMeshes[index] = extrudeHeight > 0f && mesh.Vertices != null
						? SweepPrismBuilder.Extrude(mesh, extrudeHeight, snapshot.UvScale)
						: mesh;
					return;
				}

				int k = index - greenMeshes.Length;
				if (k < blueMeshes.Length)
				{
					var fan = SweepRibbonCornerFanBuilder.Build(bluePaths[k], snapshot, blueSplineIndices[k], thickness, ct, reportProgress);
					if (fan.Vertices != null)
						blueMeshes[k] = fan;
					else if (blueFallback[k] != null)
						blueMeshes[k] = SweepMeshBuilder.Build(blueFallback[k], 0, ct, reportProgress);

					if (extrudeHeight > 0f && blueMeshes[k].Vertices != null)
						blueMeshes[k] = SweepPrismBuilder.Extrude(blueMeshes[k], extrudeHeight, snapshot.UvScale);
					return;
				}

				patchMeshes = SweepRibbonPatchBuilder.Build(split.Pieces, piecePaths, snapshot, ct, reportProgress);
				if (extrudeHeight > 0f && patchMeshes != null)
				{
					for (int p = 0; p < patchMeshes.Count; p++)
						patchMeshes[p] = SweepPrismBuilder.Extrude(patchMeshes[p], extrudeHeight, snapshot.UvScale);
				}
			}, ct);

			var results = new List<MeshInstanceData>();
			int built = 0;
			for (int k = 0; k < greenMeshes.Length; k++)
			{
				if (greenMeshes[k].Vertices != null)
					built++;
			}
			for (int k = 0; k < blueMeshes.Length; k++)
			{
				if (blueMeshes[k].Vertices != null)
					built++;
			}
			if (patchMeshes != null)
				built += patchMeshes.Count;

			for (int k = 0; k < greenMeshes.Length; k++)
			{
				var mesh = greenMeshes[k];
				if (mesh.Vertices == null)
					continue;

				results.Add(new MeshInstanceData
				{
					Name = built > 1 ? $"{snapshot.Name} {results.Count}" : snapshot.Name,
					Material = material,
					Vertices = mesh.Vertices,
					Uvs = mesh.Uvs,
					Triangles = mesh.Triangles,
					Collider = snapshot.Collider
				});
			}

			for (int k = 0; k < blueMeshes.Length; k++)
			{
				var mesh = blueMeshes[k];
				if (mesh.Vertices == null)
					continue;

				results.Add(new MeshInstanceData
				{
					Name = built > 1 ? $"{snapshot.Name} {results.Count}" : snapshot.Name,
					Material = material,
					Vertices = mesh.Vertices,
					Uvs = mesh.Uvs,
					Triangles = mesh.Triangles,
					Collider = snapshot.Collider
				});
			}

			if (patchMeshes != null)
			{
				for (int k = 0; k < patchMeshes.Count; k++)
				{
					var mesh = patchMeshes[k];
					results.Add(new MeshInstanceData
					{
						Name = built > 1 ? $"{snapshot.Name} {results.Count}" : snapshot.Name,
						Material = junctionMaterial,
						Vertices = mesh.Vertices,
						Uvs = mesh.Uvs,
						Triangles = mesh.Triangles,
						Collider = snapshot.Collider
					});
				}
			}

			Results.Value = results;

			await SyncSceneAsync(results, ct);
		}

		private async UniTask<SweepSnapshot> BuildPieceSnapshotAsync(
			SweepRibbonPiece piece,
			List<Spline> splines,
			SweepSnapshot source,
			float step,
			float maxStep,
			float maxAngleRad,
			int vpr,
			OperationScope scope,
			CancellationToken ct)
		{
			int spline = piece.Spline;
			float start = piece.StartStation;
			float end = piece.EndStation;

			float length = splines[spline].GetLength();
			var frames = await SweepNetworkFrames.BuildRangeFramesAsync(
				splines[spline],
				start,
				end,
				length,
				start,
				step,
				maxStep,
				maxAngleRad,
				vpr,
				MaxVerticesPerMesh,
				scope,
				ct);
			if (frames == null || frames.Length < 2)
				return null;

			bool capStart = start <= 1e-4f && Data.CapEnds;
			bool capEnd = end >= length - 1e-4f && Data.CapEnds;

			return new SweepSnapshot
			{
				ProfilePoints = source.ProfilePoints,
				ProfileUs = source.ProfileUs,
				ProfileSegments = source.ProfileSegments,
				ProfileClosed = source.ProfileClosed,
				Frames = new[] { frames },
				SplineClosed = new[] { false },
				WidthLut = source.WidthLut,
				WidthLuts = new[] { source.GetWidthLut(spline) },
				HeightLut = source.HeightLut,
				TwistLut = source.TwistLut,
				MaxLateralExtent = source.MaxLateralExtent,
				PreservePlanWidth = true,
				UvScale = source.UvScale,
				HeightOffset = source.HeightOffset,
				CapStartFlags = new[] { capStart },
				CapEndFlags = new[] { capEnd },
				Collider = source.Collider,
				Name = source.Name
			};
		}

		private async UniTask ComputeSingleAsync(CancellationToken ct)
		{
			SweepSnapshot snapshot = null;
			SweepRibbonPath[] ribbonPaths = null;
			bool buildStableProfile = false;
			Material material = null;
			float extrudeHeight = 0f;
			var splines = new List<Spline>();

			using (var scope = OperationScope.Start(this))
			{
				if (Data.Enabled)
				{
					snapshot = await BuildSnapshotAsync(scope, ct, splines);
					material = GetInputValue(nameof(Data.Material), Data.Material);
					extrudeHeight = ConfigureRectangleRibbon(snapshot);
					buildStableProfile = Data.Shape == ProfileShape.HalfPipe;

					if (snapshot != null && splines.Count == snapshot.Frames.Length &&
						(buildStableProfile || SweepRibbonSplitter.CanBuild(snapshot, out _)))
					{
						float step = math.max(0.05f, GetInputValue(nameof(Data.Step), Data.Step));
						ribbonPaths = new SweepRibbonPath[splines.Count];
						for (int i = 0; i < splines.Count; i++)
						{
							float length = splines[i].GetLength();
							ribbonPaths[i] = await SweepRibbonSampling.CaptureAsync(splines[i], 0f, length, step, 2, scope, ct);
						}
					}
				}

				await scope.Step(ct: ct);
			}

			var results = new List<MeshInstanceData>();

			if (snapshot != null && snapshot.Frames.Length > 0)
			{
				var meshes = new SweepMeshData[snapshot.Frames.Length];
				Action reportProgress = () => PcgComputeSystem.ReportProgress(this);

				await PcgWorkerScheduler.RunIndexedAsync(snapshot.Frames.Length, index =>
				{
					SweepMeshData mesh;
					if (ribbonPaths != null && ribbonPaths[index] != null)
					{
						if (buildStableProfile)
							mesh = SweepProfileMeshBuilder.Build(ribbonPaths[index], snapshot, index, ct, reportProgress);
						else
							mesh = SweepRibbonMeshBuilder.Build(ribbonPaths[index], snapshot, index, ct, reportProgress);
					}
					else
					{
						mesh = SweepMeshBuilder.Build(snapshot, index, ct, reportProgress);
					}

					meshes[index] = extrudeHeight > 0f && mesh.Vertices != null
						? SweepPrismBuilder.Extrude(mesh, extrudeHeight, snapshot.UvScale)
						: mesh;
				}, ct);

				int builtCount = 0;
				for (int i = 0; i < meshes.Length; i++)
				{
					if (meshes[i].Vertices != null)
						builtCount++;
				}

				for (int i = 0; i < meshes.Length; i++)
				{
					var mesh = meshes[i];
					if (mesh.Vertices == null)
						continue;

					results.Add(new MeshInstanceData
					{
						Name = builtCount > 1 ? $"{snapshot.Name} {results.Count}" : snapshot.Name,
						Material = material,
						Vertices = mesh.Vertices,
						Uvs = mesh.Uvs,
						Triangles = mesh.Triangles,
						Collider = snapshot.Collider
					});
				}

			}

			Results.Value = results;

			await SyncSceneAsync(results, ct);
		}

		private float ConfigureRectangleRibbon(SweepSnapshot snapshot)
		{
			if (snapshot == null || Data.Shape != ProfileShape.Rectangle)
				return 0f;

			float half = math.max(0.01f, GetInputValue(nameof(Data.Width), Data.Width)) * 0.5f;
			snapshot.ProfilePoints = new[] { new float2(-half, 0f), new float2(half, 0f) };
			snapshot.ProfileUs = new[] { 0f, 1f };
			snapshot.ProfileSegments = new[] { 0, 1 };
			snapshot.ProfileClosed = false;
			snapshot.MaxLateralExtent = half * MaxWidthLut(snapshot);
			return math.max(0.01f, GetInputValue(nameof(Data.Height), Data.Height));
		}

		private async UniTask<SweepSnapshot> BuildSnapshotAsync(
			OperationScope scope,
			CancellationToken ct,
			List<Spline> accepted = null)
		{
			var profile = ResolveProfile(Warn);
			if (profile == null || profile.Points == null || profile.Points.Length < 2)
				return null;

			var widthLut = BuildLut(Data.WidthByT, true);
			var heightLut = BuildLut(Data.HeightByT, true);
			var twistLut = BuildLut(Data.TwistByT, false);

			float step = math.max(0.05f, GetInputValue(nameof(Data.Step), Data.Step));
			float maxStep = math.max(step, GetInputValue(nameof(Data.MaxStep), Data.MaxStep));
			float maxAngle = math.clamp(GetInputValue(nameof(Data.MaxAngle), Data.MaxAngle), 0.5f, 180f);
			float uvScale = GetInputValue(nameof(Data.UvScale), Data.UvScale);
			float heightOffset = GetInputValue(nameof(Data.HeightOffset), Data.HeightOffset);
			string name = GetInputValue(nameof(Data.Name), Data.Name);

			int vpr = profile.Points.Length;

			float maxAbsProfile = 0f;
			float minProfileX = float.MaxValue;
			float maxProfileX = float.MinValue;
			for (int i = 0; i < profile.Points.Length; i++)
			{
				maxAbsProfile = math.max(maxAbsProfile, math.length(profile.Points[i]));
				minProfileX = math.min(minProfileX, profile.Points[i].x);
				maxProfileX = math.max(maxProfileX, profile.Points[i].x);
			}
			float profileWidth = math.max(0.01f, maxProfileX - minProfileX);

			var framesList = new List<SweepFrame[]>();
			var closedList = new List<bool>();
			var splineWidthLuts = new List<float[]>();

			var splinesInput = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesInput != null)
			{
				foreach (var splines in splinesInput)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline == null || spline.Count < 2)
							continue;

						float length = spline.GetLength();
						if (!IsFinite(length) || length <= 1e-4f)
							continue;

						var frames = await BuildFramesAsync(spline, length, step, maxStep, maxAngle, vpr, scope, ct);
						if (frames == null)
							continue;

						framesList.Add(frames);
						closedList.Add(spline.Closed);
						splineWidthLuts.Add(await BuildSplineWidthLutAsync(spline, profileWidth, widthLut, scope, ct));
						accepted?.Add(spline);
					}
				}
			}

			float maxWidthMul = MaxLut(widthLut);
			for (int i = 0; i < splineWidthLuts.Count; i++)
				maxWidthMul = math.max(maxWidthMul, MaxLut(splineWidthLuts[i]));
			float maxMul = math.max(maxWidthMul, MaxLut(heightLut));
			float lateralExtent = maxAbsProfile * maxMul;

			var capStartFlags = new bool[framesList.Count];
			var capEndFlags = new bool[framesList.Count];
			for (int i = 0; i < framesList.Count; i++)
			{
				capStartFlags[i] = Data.CapEnds;
				capEndFlags[i] = Data.CapEnds;
			}

			return new SweepSnapshot
			{
				ProfilePoints = (float2[])profile.Points.Clone(),
				ProfileUs = (float[])profile.Us.Clone(),
				ProfileSegments = (int[])profile.Segments.Clone(),
				ProfileClosed = profile.Closed,
				Frames = framesList.ToArray(),
				SplineClosed = closedList.ToArray(),
				WidthLut = widthLut,
				WidthLuts = splineWidthLuts.ToArray(),
				HeightLut = heightLut,
				TwistLut = twistLut,
				MaxLateralExtent = lateralExtent,
				UvScale = uvScale,
				HeightOffset = heightOffset,
				CapStartFlags = capStartFlags,
				CapEndFlags = capEndFlags,
				Collider = Data.Collider,
				Name = name
			};
		}

		private async UniTask<SweepFrame[]> BuildFramesAsync(
			Spline spline,
			float length,
			float step,
			float maxStep,
			float maxAngle,
			int vpr,
			OperationScope scope,
			CancellationToken ct)
		{
			bool closed = spline.Closed;
			int quantCount = (int)math.ceil(length / step);
			quantCount = closed ? math.max(3, quantCount) : math.max(1, quantCount);

			if ((long)(quantCount + 1) * vpr > MaxVerticesPerMesh)
			{
				Debug.LogError($"[Sweep Spline] A spline would build {(long)(quantCount + 1) * vpr} vertices which exceeds the {MaxVerticesPerMesh} limit; it was skipped.");
				return null;
			}

			var quantFrames = new SweepFrame[quantCount + 1];
			for (int q = 0; q <= quantCount; q++)
			{
				float distance = length * q / quantCount;
				if (!TryBuildFrame(spline, distance, length, out quantFrames[q]))
					return null;
				await scope.Step(ct: ct);
			}

			var turns = new float[quantCount];
			var rolls = new float[quantCount];
			for (int q = 0; q < quantCount; q++)
			{
				float3 t0 = math.normalizesafe(quantFrames[q].Tangent, new float3(0f, 0f, 1f));
				float3 t1 = math.normalizesafe(quantFrames[q + 1].Tangent, new float3(0f, 0f, 1f));
				turns[q] = math.acos(math.clamp(math.dot(t0, t1), -1f, 1f));

				float3 u0 = math.normalizesafe(quantFrames[q].Up, new float3(0f, 1f, 0f));
				float3 u1 = math.normalizesafe(quantFrames[q + 1].Up, new float3(0f, 1f, 0f));
				rolls[q] = math.acos(math.clamp(math.dot(u0, u1), -1f, 1f));
				await scope.Step(ct: ct);
			}

			float maxAngleRad = math.radians(maxAngle);
			var frames = new List<SweepFrame>(quantCount + 1);
			frames.Add(quantFrames[0]);

			int current = 0;
			while (current < quantCount)
			{
				int next = current + 1;
				float turnSum = turns[current];
				float rollSum = rolls[current];
				while (next < quantCount)
				{
					float candidateTurn = turnSum + turns[next];
					float candidateRoll = rollSum + rolls[next];
					float candidateLength = quantFrames[next + 1].Distance - quantFrames[current].Distance;
					if (candidateTurn > maxAngleRad || candidateRoll > maxAngleRad || candidateLength > maxStep)
						break;
					turnSum = candidateTurn;
					rollSum = candidateRoll;
					next++;
				}

				frames.Add(quantFrames[next]);
				current = next;
				await scope.Step(ct: ct);
			}

			if (closed)
			{
				var seam = frames[0];
				seam.Distance = length;
				seam.T = 1f;
				frames[frames.Count - 1] = seam;
			}

			return frames.ToArray();
		}

		private bool TryBuildFrame(Spline spline, float distance, float length, out SweepFrame frame)
		{
			frame = default;
			float t = math.clamp(spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized), 0f, 1f);
			float3 position = spline.EvaluatePosition(t);
			float3 tangent = spline.EvaluateTangent(t);
			float3 up = spline.EvaluateUpVector(t);

			if (!IsFinite(position) || !IsFinite(tangent) || !IsFinite(up))
				return false;

			frame = new SweepFrame
			{
				Position = position,
				Tangent = tangent,
				Up = up,
				T = distance / length,
				Distance = distance
			};
			return true;
		}

		private SweepProfile ResolveProfile(Action<string> warn)
		{
			var connected = GetInputValue(nameof(Data.Profile), Data.Profile);
			if (connected != null)
				return connected;

			float width = GetInputValue(nameof(Data.Width), Data.Width);
			float height = GetInputValue(nameof(Data.Height), Data.Height);
			return SweepProfileBuilder.Build(Data.Shape, width, height, Data.Sides, Data.CustomPoints, Data.CustomClosed, warn);
		}

		private float[] BuildLut(AnimationCurve curve, bool clampPositive)
		{
			var lut = new float[LutSize];
			for (int i = 0; i < LutSize; i++)
			{
				float t = i / (float)(LutSize - 1);
				float value = curve.Evaluate(t);
				lut[i] = clampPositive ? math.max(0.001f, value) : value;
			}
			return lut;
		}

		private static async UniTask<float[]> BuildSplineWidthLutAsync(
			Spline spline,
			float profileWidth,
			float[] baseLut,
			OperationScope scope,
			CancellationToken ct)
		{
			if (!SplineWidthUtility.TryEvaluate(spline, 0f, out _))
				return (float[])baseLut.Clone();

			var result = new float[baseLut.Length];
			for (int i = 0; i < result.Length; i++)
			{
				float t = result.Length > 1 ? (float)i / (result.Length - 1) : 0f;
				float absoluteWidth = math.max(0.01f, SplineWidthUtility.Evaluate(spline, t, profileWidth));
				result[i] = baseLut[i] * absoluteWidth / profileWidth;
				await scope.Step(ct: ct);
			}

			return result;
		}

		private static float MaxWidthLut(SweepSnapshot snapshot)
		{
			float max = MaxLut(snapshot.WidthLut);
			if (snapshot.WidthLuts == null)
				return max;

			for (int i = 0; i < snapshot.WidthLuts.Length; i++)
				max = math.max(max, MaxLut(snapshot.WidthLuts[i]));
			return max;
		}

		private static float MaxLut(float[] lut)
		{
			float max = lut[0];
			for (int i = 1; i < lut.Length; i++)
				max = math.max(max, lut[i]);
			return max;
		}

		private async UniTask SyncSceneAsync(List<MeshInstanceData> results, CancellationToken ct)
		{
			var container = InstanceMakerContainer;
			if (container == null)
				return;

			if (!(PcgComputeSystem.IsGenerating || IsPreviewLocal || IsPreviewGlobal))
				return;

			if (container.HasOwnedObjects(Address.ToKey()))
				await container.RemoveInstances(Address.ToKey(), ct);

			if (results.Count == 0)
				return;

			container.Begin();
			try
			{
				await container.AddInstances(Address.ToKey(), null, results, ct);
			}
			finally
			{
				container.End();
			}
		}

		public async UniTask ClearInstancesAsync(CancellationToken ct = default)
		{
			var container = InstanceMakerContainer;
			if (container != null && container.HasOwnedObjects(Address.ToKey()))
				await container.RemoveInstances(Address.ToKey(), ct);
			LastComputedVersion = 0;
		}

		public override int GetVersionSalt()
		{
			unchecked
			{
				int hash = 17;
				hash = (hash * 397) ^ unchecked((int)0x57555001);

				var profile = ResolveProfile(null);
				if (profile != null)
					hash = (hash * 397) ^ profile.GetContentHash();

				hash = (hash * 397) ^ CurveHash(Data.WidthByT);
				hash = (hash * 397) ^ CurveHash(Data.HeightByT);
				hash = (hash * 397) ^ CurveHash(Data.TwistByT);
				hash = (hash * 397) ^ Data.Step.GetHashCode();
				hash = (hash * 397) ^ Data.MaxStep.GetHashCode();
				hash = (hash * 397) ^ Data.MaxAngle.GetHashCode();
				hash = (hash * 397) ^ Data.MergeThickness.GetHashCode();
				hash = (hash * 397) ^ (Data.MergeIntersections ? 1 : 0);
				hash = (hash * 397) ^ Data.Sides;
				return hash;
			}
		}

		public override void DrawPreview(Transform transform)
		{
			if (!Data.MergeIntersections)
				return;

			var color = Gizmos.color;
			var matrix = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;

			if (Data.ShowAllCuts && _previewDebugCuts != null)
			{
				for (int i = 0; i < _previewDebugCuts.Length; i++)
				{
					Gizmos.color = _previewDebugState[i] == 1 ? Color.red : _previewDebugState[i] == 2 ? Color.blue : Color.green;
					Gizmos.DrawLine(_previewDebugCuts[i][0], _previewDebugCuts[i][1]);
				}

				Gizmos.matrix = matrix;
				Gizmos.color = color;
				return;
			}

			if (!Data.ShowIntersections)
			{
				Gizmos.matrix = matrix;
				Gizmos.color = color;
				return;
			}

			if (_previewFreeSplines != null)
			{
				Gizmos.color = Color.cyan;
				for (int i = 0; i < _previewFreeSplines.Length; i++)
				{
					var polyline = _previewFreeSplines[i];
					for (int p = 0; p + 1 < polyline.Length; p++)
						Gizmos.DrawLine(polyline[p], polyline[p + 1]);
				}
			}

			if (_previewCutChords != null)
			{
				Gizmos.color = Color.green;
				for (int i = 0; i < _previewCutChords.Length; i++)
					Gizmos.DrawLine(_previewCutChords[i][0], _previewCutChords[i][1]);
			}

			if (_previewBlackPoints != null)
			{
				float radius = math.max(0.05f, GetInputValue(nameof(Data.Width), Data.Width) * 0.06f);
				Gizmos.color = Color.black;
				for (int i = 0; i < _previewBlackPoints.Length; i++)
					Gizmos.DrawSphere(_previewBlackPoints[i], radius);
			}

			Gizmos.matrix = matrix;
			Gizmos.color = color;
		}

		private void Warn(string message)
		{
			Debug.LogWarning($"[Sweep Spline] {message}");
		}

		private int TriangleCount()
		{
			int total = 0;
			for (int i = 0; i < Results.Value.Count; i++)
			{
				var triangles = Results.Value[i].Triangles;
				if (triangles != null)
					total += triangles.Length / 3;
			}
			return total;
		}

		private static int CurveHash(AnimationCurve curve)
		{
			unchecked
			{
				int hash = 17;
				if (curve == null)
					return hash;

				var keys = curve.keys;
				hash = (hash * 397) ^ keys.Length;
				for (int i = 0; i < keys.Length; i++)
				{
					hash = (hash * 397) ^ keys[i].time.GetHashCode();
					hash = (hash * 397) ^ keys[i].value.GetHashCode();
					hash = (hash * 397) ^ keys[i].inTangent.GetHashCode();
					hash = (hash * 397) ^ keys[i].outTangent.GetHashCode();
				}
				return hash;
			}
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

		private static bool IsFinite(float3 value)
		{
			return math.all(math.isfinite(value));
		}
	}
}
