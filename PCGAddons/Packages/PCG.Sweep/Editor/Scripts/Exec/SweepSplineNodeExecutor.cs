using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Instances;
using PCG.Splines;
using PCG.Terrains;
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
			float step = 0f;
			float thickness = 0f;

			using (var scope = OperationScope.Start(this))
			{
				snapshot = BuildSnapshot(ct, splines);
				step = math.max(0.05f, GetInputValue(nameof(Data.Step), Data.Step));
				thickness = math.max(0f, GetInputValue(nameof(Data.MergeThickness), Data.MergeThickness));

				await scope.Step(ct: ct);
			}

			if (snapshot == null || snapshot.Frames.Length == 0 || splines.Count != snapshot.Frames.Length)
			{
				Results.Value = new List<MeshInstanceData>();
				await SyncSceneAsync(Results.Value, ct);
				return;
			}

			if (!SweepRibbonSplitter.CanBuild(snapshot, out string profileFailure))
			{
				Debug.LogWarning($"[Sweep Spline] Merge Intersections needs a Ribbon profile: node {Address}, code {profileFailure}; the sweep was built normally.");
				await ComputeSingleAsync(ct);
				return;
			}

			Action reportProgress = () => PcgComputeSystem.ReportProgress(this);
			SweepRibbonSplitResult split = null;

			await UniTask.RunOnThreadPool(() =>
			{
				split = SweepRibbonSplitter.Split(snapshot, splines, step, thickness, ct, reportProgress);
			}, true, ct);

			await UniTaskEditor.SwitchToEditorThread();

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
			var bluePieces = new List<SweepRibbonPiece>();
			var blueFallback = new List<SweepSnapshot>();
			for (int p = 0; p < split.Pieces.Count; p++)
			{
				var piece = split.Pieces[p];
				if (piece.State == SweepRibbonPiece.Green)
				{
					var pieceSnap = BuildPieceSnapshot(piece, splines, snapshot, step, maxStep, maxAngleRad, vpr);
					if (pieceSnap != null)
						greenSnaps.Add(pieceSnap);
				}
				else if (piece.State == SweepRibbonPiece.Blue)
				{
					bluePieces.Add(piece);
					blueFallback.Add(BuildPieceSnapshot(piece, splines, snapshot, step, maxStep, maxAngleRad, vpr));
				}
			}

			var greenMeshes = new SweepMeshData[greenSnaps.Count];
			var blueMeshes = new SweepMeshData[bluePieces.Count];
			List<SweepMeshData> patchMeshes = null;
			await UniTask.RunOnThreadPool(() =>
			{
				for (int k = 0; k < greenSnaps.Count; k++)
					greenMeshes[k] = SweepMeshBuilder.Build(greenSnaps[k], 0, ct, reportProgress);

				for (int k = 0; k < bluePieces.Count; k++)
				{
					var piece = bluePieces[k];
					var fan = SweepRibbonCornerFanBuilder.Build(splines[piece.Spline], piece.StartStation, piece.EndStation, snapshot, step, ct, reportProgress);
					if (fan.Vertices != null)
						blueMeshes[k] = fan;
					else if (blueFallback[k] != null)
						blueMeshes[k] = SweepMeshBuilder.Build(blueFallback[k], 0, ct, reportProgress);
				}

				patchMeshes = SweepRibbonPatchBuilder.Build(split.Pieces, splines, snapshot, step);
			}, true, ct);

			await UniTaskEditor.SwitchToEditorThread();

			var results = new List<MeshInstanceData>();
			bool outOfBounds = split.TerrainOutOfBounds;
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

				outOfBounds |= mesh.TerrainOutOfBounds;
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

				outOfBounds |= mesh.TerrainOutOfBounds;
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

			if (outOfBounds)
				Debug.LogWarning("[Sweep Spline] Part of the sweep is outside the terrain and keeps the spline height.");

			Results.Value = results;

			await SyncSceneAsync(results, ct);
		}

		private SweepSnapshot BuildPieceSnapshot(SweepRibbonPiece piece, List<Spline> splines, SweepSnapshot source, float step, float maxStep, float maxAngleRad, int vpr)
		{
			int spline = piece.Spline;
			float start = piece.StartStation;
			float end = piece.EndStation;

			float length = splines[spline].GetLength();
			var frames = SweepNetworkFrames.BuildRangeFrames(splines[spline], start, end, length, start, step, maxStep, maxAngleRad, vpr, MaxVerticesPerMesh);
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
				HeightLut = source.HeightLut,
				TwistLut = source.TwistLut,
				Terrain = source.Terrain,
				MaxLateralExtent = source.MaxLateralExtent,
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
			Material material = null;

			using (var scope = OperationScope.Start(this))
			{
				if (Data.Enabled)
				{
					snapshot = BuildSnapshot(ct);
					material = GetInputValue(nameof(Data.Material), Data.Material);
				}

				await scope.Step(ct: ct);
			}

			var results = new List<MeshInstanceData>();

			if (snapshot != null && snapshot.Frames.Length > 0)
			{
				var meshes = new SweepMeshData[snapshot.Frames.Length];
				var tasks = new List<UniTask>(snapshot.Frames.Length);
				Action reportProgress = () => PcgComputeSystem.ReportProgress(this);

				for (int i = 0; i < snapshot.Frames.Length; i++)
				{
					int index = i;
					tasks.Add(UniTask.RunOnThreadPool(() =>
					{
						meshes[index] = SweepMeshBuilder.Build(snapshot, index, ct, reportProgress);
					}, true, ct));
				}

				await UniTask.WhenAll(tasks);
				await UniTaskEditor.SwitchToEditorThread();

				bool outOfBounds = false;
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

					outOfBounds |= mesh.TerrainOutOfBounds;
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

				if (outOfBounds)
					Debug.LogWarning("[Sweep Spline] Part of the sweep is outside the terrain and keeps the spline height.");
			}

			Results.Value = results;

			await SyncSceneAsync(results, ct);
		}

		private SweepSnapshot BuildSnapshot(CancellationToken ct, List<Spline> accepted = null)
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
			for (int i = 0; i < profile.Points.Length; i++)
				maxAbsProfile = math.max(maxAbsProfile, math.length(profile.Points[i]));

			float maxMul = math.max(MaxLut(widthLut), MaxLut(heightLut));
			float lateralExtent = maxAbsProfile * maxMul;

			var framesList = new List<SweepFrame[]>();
			var closedList = new List<bool>();

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

						var frames = BuildFrames(spline, length, step, maxStep, maxAngle, vpr);
						if (frames == null)
							continue;

						framesList.Add(frames);
						closedList.Add(spline.Closed);
						accepted?.Add(spline);
					}
				}
			}

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
				HeightLut = heightLut,
				TwistLut = twistLut,
				Terrain = null,
				MaxLateralExtent = lateralExtent,
				UvScale = uvScale,
				HeightOffset = heightOffset,
				CapStartFlags = capStartFlags,
				CapEndFlags = capEndFlags,
				Collider = Data.Collider,
				Name = name
			};
		}

		private SweepFrame[] BuildFrames(Spline spline, float length, float step, float maxStep, float maxAngle, int vpr)
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
			return SweepProfileBuilder.Build(Data.Shape, width, height, Data.CustomPoints, Data.CustomClosed, warn);
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

				var profile = ResolveProfile(null);
				if (profile != null)
					hash = (hash * 397) ^ profile.GetContentHash();

				hash = (hash * 397) ^ CurveHash(Data.WidthByT);
				hash = (hash * 397) ^ CurveHash(Data.HeightByT);
				hash = (hash * 397) ^ CurveHash(Data.TwistByT);
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
