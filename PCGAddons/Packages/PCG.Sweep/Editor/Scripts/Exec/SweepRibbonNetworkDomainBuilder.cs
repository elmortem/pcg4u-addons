using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Polygons;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRibbonNetworkDomainBuilder
	{
		private const float MinimumProjectionRatio = 0.05f;
		private const float MinimumTriangleArea = 1e-10f;

		internal static bool CanBuild(SweepNetworkSnapshot snapshot, out string failure)
		{
			return CanBuild(snapshot, false, out failure);
		}

		internal static bool CanBuildHeightfield(SweepNetworkSnapshot snapshot, out string failure)
		{
			return CanBuild(snapshot, true, out failure);
		}

		private static bool CanBuild(SweepNetworkSnapshot snapshot, bool heightfield, out string failure)
		{
			failure = null;
			if (snapshot == null || snapshot.Pieces == null)
			{
				failure = "GlobalSnapshotMissing";
				return false;
			}
			SweepSnapshot pieces = snapshot.Pieces;
			if (!TryGetProfileEndpoints(pieces, heightfield, out int first, out int second, out failure))
				return false;
			float2 a = pieces.ProfilePoints[first];
			float2 b = pieces.ProfilePoints[second];
			float halfWidth = math.max(math.abs(a.x), math.abs(b.x));
			float symmetryTolerance = math.max(1e-6f, halfWidth * 1e-5f);
			if (!heightfield && (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || math.abs(a.y) > symmetryTolerance || math.abs(b.y) > symmetryTolerance || math.abs(a.x - b.x) <= symmetryTolerance))
			{
				failure = "GlobalBuiltInRibbonRequired";
				return false;
			}
			if (pieces.Frames == null || snapshot.PieceMeshes == null || snapshot.PieceMeshes.Length != pieces.Frames.Length || snapshot.Junctions == null)
			{
				failure = "GlobalNetworkDataMissing";
				return false;
			}
			for (int piece = 0; piece < snapshot.PieceMeshes.Length; piece++)
			{
				SweepMeshData mesh = snapshot.PieceMeshes[piece];
				if (pieces.Frames[piece] != null && (mesh.Vertices == null || mesh.Vertices.Length < 3 || mesh.Uvs == null || mesh.Triangles == null || mesh.Triangles.Length < 3 || mesh.Uvs.Length != mesh.Vertices.Length || mesh.Triangles.Length % 3 != 0))
				{
					failure = "GlobalPieceMeshInvalid-" + piece;
					return false;
				}
				if (mesh.Vertices == null)
					continue;
				for (int triangle = 0; triangle < mesh.Triangles.Length; triangle += 3)
				{
					int ia = mesh.Triangles[triangle];
					int ib = mesh.Triangles[triangle + 1];
					int ic = mesh.Triangles[triangle + 2];
					if (ia < 0 || ib < 0 || ic < 0 || ia >= mesh.Vertices.Length || ib >= mesh.Vertices.Length || ic >= mesh.Vertices.Length)
					{
						failure = "GlobalPieceTriangleInvalid-" + piece;
						return false;
					}
					if (!ProjectionValid(ToFloat3(mesh.Vertices[ia]), ToFloat3(mesh.Vertices[ib]), ToFloat3(mesh.Vertices[ic])))
					{
						failure = "GlobalPieceFoldover-" + piece + "-" + triangle / 3;
						return false;
					}
				}
			}
			return true;
		}

		internal static bool TryBuild(SweepNetworkSnapshot snapshot, CancellationToken ct, Action reportProgress, out SweepRibbonNetworkDomain domain, out string failure)
		{
			return TryBuild(snapshot, false, ct, reportProgress, out domain, out failure);
		}

		internal static bool TryBuildHeightfield(SweepNetworkSnapshot snapshot, CancellationToken ct, Action reportProgress, out SweepRibbonNetworkDomain domain, out string failure)
		{
			domain = null;
			if (!CanBuild(snapshot, true, out failure))
				return false;
			if (!TryBuildHeightfieldFootprint(snapshot, ct, reportProgress, out SweepNetworkSnapshot footprint, out failure))
				return false;
			if (!TryBuild(footprint, false, ct, reportProgress, out domain, out failure))
				return false;
			if (!TryCollectHeightfieldSources(snapshot, ct, reportProgress, out SortedDictionary<int, List<SweepRibbonSourceTriangle>> sourcesByComponent, out failure))
				return false;
			for (int componentIndex = 0; componentIndex < domain.Components.Length; componentIndex++)
			{
				SweepRibbonNetworkDomainComponent component = domain.Components[componentIndex];
				if (!sourcesByComponent.TryGetValue(component.NetworkComponent, out List<SweepRibbonSourceTriangle> sources) || sources.Count == 0)
				{
					failure = "GlobalHeightfieldSourcesMissing-Component-" + component.NetworkComponent;
					return false;
				}
				SweepRibbonSourceTriangle[] sourceArray = sources.ToArray();
				bool terrainOutOfBounds = false;
				for (int source = 0; source < sourceArray.Length; source++)
					terrainOutOfBounds |= sourceArray[source].TerrainOutOfBounds;
				component.Sources = sourceArray;
				component.TerrainOutOfBounds = terrainOutOfBounds;
			}
			return true;
		}

		private static bool TryBuild(SweepNetworkSnapshot snapshot, bool heightfield, CancellationToken ct, Action reportProgress, out SweepRibbonNetworkDomain domain, out string failure)
		{
			domain = null;
			if (!CanBuild(snapshot, heightfield, out failure))
				return false;

			SweepSnapshot pieces = snapshot.Pieces;
			int pieceCount = pieces.Frames.Length;
			var parents = new int[pieceCount];
			var hasStartArm = new bool[pieceCount];
			var hasEndArm = new bool[pieceCount];
			for (int piece = 0; piece < pieceCount; piece++)
				parents[piece] = piece;
			for (int junctionIndex = 0; junctionIndex < snapshot.Junctions.Length; junctionIndex++)
			{
				SweepNetworkJunction junction = snapshot.Junctions[junctionIndex];
				if (junction == null || junction.Arms == null)
				{
					failure = "GlobalJunctionInvalid-" + junctionIndex;
					return false;
				}
				int firstPiece = -1;
				for (int armIndex = 0; armIndex < junction.Arms.Length; armIndex++)
				{
					SweepNetworkArm arm = junction.Arms[armIndex];
					if (arm == null || arm.PieceIndex < 0 || arm.PieceIndex >= pieceCount)
					{
						failure = "GlobalArmInvalid-" + junctionIndex + "-" + armIndex;
						return false;
					}
					if (arm.AtPieceStart)
						hasStartArm[arm.PieceIndex] = true;
					else
						hasEndArm[arm.PieceIndex] = true;
					if (firstPiece < 0)
						firstPiece = arm.PieceIndex;
					else
						Union(parents, firstPiece, arm.PieceIndex);
				}
			}
			for (int piece = 0; piece < pieceCount; piece++)
				parents[piece] = Root(parents, piece);

			var sourcesByComponent = new SortedDictionary<int, List<SweepRibbonSourceTriangle>>();
			var terminalsByComponent = new SortedDictionary<int, List<SweepRibbonTerminalSegment>>();
			int sourceOrder = 0;
			if (!TryGetProfileEndpoints(pieces, heightfield, out int firstProfile, out int secondProfile, out failure))
				return false;
			for (int piece = 0; piece < pieceCount; piece++)
			{
				ct.ThrowIfCancellationRequested();
				int component = parents[piece];
				List<SweepRibbonSourceTriangle> sources = GetList(sourcesByComponent, component);
				List<SweepRibbonTerminalSegment> terminals = GetList(terminalsByComponent, component);
				SweepMeshData mesh = snapshot.PieceMeshes[piece];
				if (mesh.Vertices == null)
					continue;
				for (int triangle = 0; triangle < mesh.Triangles.Length; triangle += 3)
				{
					int ia = mesh.Triangles[triangle];
					int ib = mesh.Triangles[triangle + 1];
					int ic = mesh.Triangles[triangle + 2];
					if (!TryAddSource(component, true, ToFloat3(mesh.Vertices[ia]), ToFloat3(mesh.Vertices[ib]), ToFloat3(mesh.Vertices[ic]), mesh.Uvs[ia], mesh.Uvs[ib], mesh.Uvs[ic], mesh.TerrainOutOfBounds, ref sourceOrder, sources, out failure))
					{
						failure += "-Piece-" + piece + "-" + triangle / 3;
						return false;
					}
				}
				if (!hasStartArm[piece] && !TryAddTerminal(component, mesh.StartRing, firstProfile, secondProfile, terminals, out failure))
				{
					failure += "-Start-" + piece;
					return false;
				}
				if (!hasEndArm[piece] && !TryAddTerminal(component, mesh.EndRing, firstProfile, secondProfile, terminals, out failure))
				{
					failure += "-End-" + piece;
					return false;
				}
				reportProgress?.Invoke();
			}

			for (int junctionIndex = 0; junctionIndex < snapshot.Junctions.Length; junctionIndex++)
			{
				SweepNetworkJunction junction = snapshot.Junctions[junctionIndex];
				for (int armIndex = 0; armIndex < junction.Arms.Length; armIndex++)
				{
					ct.ThrowIfCancellationRequested();
					SweepNetworkArm arm = junction.Arms[armIndex];
					int component = parents[arm.PieceIndex];
					List<SweepRibbonSourceTriangle> sources = GetList(sourcesByComponent, component);
					if (!TryAddApproach(snapshot, arm, component, firstProfile, secondProfile, heightfield, ref sourceOrder, sources, out failure))
					{
						failure += "-Junction-" + junctionIndex + "-Arm-" + armIndex;
						return false;
					}
					reportProgress?.Invoke();
				}
			}

			float width = math.abs(pieces.ProfilePoints[firstProfile].x - pieces.ProfilePoints[secondProfile].x);
			float heightTolerance = math.max(0.002f, width * 0.0025f);
			float sourceCellSize = math.max(0.05f, math.max(snapshot.Step, pieces.MaxLateralExtent));
			var built = new List<SweepRibbonNetworkDomainComponent>();
			foreach (var pair in sourcesByComponent)
			{
				ct.ThrowIfCancellationRequested();
				List<SweepRibbonSourceTriangle> sources = pair.Value;
				if (sources.Count == 0)
					continue;
				if (!SweepRibbonPolygonUnion.TryUnion(sources, out List<Polygon2D> polygons, out failure))
				{
					failure += "-Component-" + pair.Key;
					return false;
				}
				SweepRibbonSourceTriangle[] sourceArray = sources.ToArray();
				List<SweepRibbonTerminalSegment> terminals = terminalsByComponent.TryGetValue(pair.Key, out List<SweepRibbonTerminalSegment> foundTerminals)
					? foundTerminals
					: new List<SweepRibbonTerminalSegment>();
				bool terrainOutOfBounds = false;
				for (int sourceIndex = 0; sourceIndex < sourceArray.Length; sourceIndex++)
					terrainOutOfBounds |= sourceArray[sourceIndex].TerrainOutOfBounds;
				for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
				{
					Polygon2D polygon = polygons[polygonIndex];
					if (!SweepRibbonNetworkTriangulator.TryTriangulate(polygon, sourceArray, ct, reportProgress, out float2[] planVertices, out int[] triangles, out failure))
					{
						failure += "-Component-" + pair.Key + "-Polygon-" + polygonIndex;
						return false;
					}
					var holeKinds = new SweepRibbonBoundaryKind[polygon.Holes.Count][];
					for (int hole = 0; hole < polygon.Holes.Count; hole++)
						holeKinds[hole] = ClassifyBoundary(polygon.Holes[hole], terminals);
					built.Add(new SweepRibbonNetworkDomainComponent
					{
						NetworkComponent = pair.Key,
						Polygon = polygon,
						PlanVertices = planVertices,
						Triangles = triangles,
						OuterEdgeKinds = ClassifyBoundary(polygon.Outer, terminals),
						HoleEdgeKinds = holeKinds,
						Sources = sourceArray,
						TerrainOutOfBounds = terrainOutOfBounds
					});
				}
			}

			if (built.Count == 0)
			{
				failure = "GlobalDomainEmpty";
				return false;
			}
			domain = new SweepRibbonNetworkDomain
			{
				Components = built.ToArray(),
				HeightTolerance = heightTolerance,
				SourceCellSize = sourceCellSize
			};
			return true;
		}

		private static bool TryBuildHeightfieldFootprint(SweepNetworkSnapshot source, CancellationToken ct, Action reportProgress, out SweepNetworkSnapshot footprint, out string failure)
		{
			footprint = null;
			if (!TryGetProfileEndpoints(source.Pieces, true, out int first, out int second, out failure))
				return false;
			SweepSnapshot sourcePieces = source.Pieces;
			var pieces = new SweepSnapshot
			{
				ProfilePoints = new[]
				{
					new float2(sourcePieces.ProfilePoints[first].x, 0f),
					new float2(sourcePieces.ProfilePoints[second].x, 0f)
				},
				ProfileUs = new[] { sourcePieces.ProfileUs[first], sourcePieces.ProfileUs[second] },
				ProfileSegments = new[] { 0, 1 },
				ProfileClosed = false,
				Frames = sourcePieces.Frames,
				SplineClosed = sourcePieces.SplineClosed,
				WidthLut = sourcePieces.WidthLut,
				HeightLut = sourcePieces.HeightLut,
				TwistLut = sourcePieces.TwistLut,
				Terrain = sourcePieces.Terrain,
				MaxLateralExtent = sourcePieces.MaxLateralExtent,
				UvScale = sourcePieces.UvScale,
				HeightOffset = sourcePieces.HeightOffset,
				CapStartFlags = sourcePieces.CapStartFlags,
				CapEndFlags = sourcePieces.CapEndFlags,
				Collider = sourcePieces.Collider,
				Name = sourcePieces.Name
			};
			Action progress = reportProgress ?? (() => { });
			var meshes = new SweepMeshData[pieces.Frames.Length];
			var startRings = new Vector3[meshes.Length][];
			var endRings = new Vector3[meshes.Length][];
			for (int piece = 0; piece < meshes.Length; piece++)
			{
				ct.ThrowIfCancellationRequested();
				if (pieces.Frames[piece] != null)
					meshes[piece] = SweepMeshBuilder.Build(pieces, piece, ct, progress);
				startRings[piece] = meshes[piece].StartRing;
				endRings[piece] = meshes[piece].EndRing;
				progress();
			}
			footprint = new SweepNetworkSnapshot
			{
				Pieces = pieces,
				PieceMeshes = meshes,
				Junctions = source.Junctions,
				PieceStartRings = startRings,
				PieceEndRings = endRings,
				Step = source.Step,
				MaxAngleRad = source.MaxAngleRad,
				UvScale = source.UvScale,
				HeightOffset = source.HeightOffset,
				Collider = source.Collider,
				CapEnds = source.CapEnds,
				Name = source.Name,
				JunctionMaterial = source.JunctionMaterial
			};
			return true;
		}

		private static bool TryCollectHeightfieldSources(SweepNetworkSnapshot snapshot, CancellationToken ct, Action reportProgress, out SortedDictionary<int, List<SweepRibbonSourceTriangle>> result, out string failure)
		{
			result = new SortedDictionary<int, List<SweepRibbonSourceTriangle>>();
			failure = null;
			int pieceCount = snapshot.Pieces.Frames.Length;
			var parents = new int[pieceCount];
			for (int piece = 0; piece < pieceCount; piece++)
				parents[piece] = piece;
			for (int junctionIndex = 0; junctionIndex < snapshot.Junctions.Length; junctionIndex++)
			{
				SweepNetworkJunction junction = snapshot.Junctions[junctionIndex];
				if (junction == null || junction.Arms == null)
				{
					failure = "GlobalHeightfieldJunctionInvalid-" + junctionIndex;
					return false;
				}
				int firstPiece = -1;
				for (int armIndex = 0; armIndex < junction.Arms.Length; armIndex++)
				{
					SweepNetworkArm arm = junction.Arms[armIndex];
					if (arm == null || arm.PieceIndex < 0 || arm.PieceIndex >= pieceCount)
					{
						failure = "GlobalHeightfieldArmInvalid-" + junctionIndex + "-" + armIndex;
						return false;
					}
					if (firstPiece < 0)
						firstPiece = arm.PieceIndex;
					else
						Union(parents, firstPiece, arm.PieceIndex);
				}
			}
			for (int piece = 0; piece < parents.Length; piece++)
				parents[piece] = Root(parents, piece);

			int sourceOrder = 0;
			for (int piece = 0; piece < pieceCount; piece++)
			{
				ct.ThrowIfCancellationRequested();
				SweepMeshData mesh = snapshot.PieceMeshes[piece];
				if (mesh.Vertices == null)
					continue;
				List<SweepRibbonSourceTriangle> sources = GetList(result, parents[piece]);
				for (int triangle = 0; triangle < mesh.Triangles.Length; triangle += 3)
				{
					int ia = mesh.Triangles[triangle];
					int ib = mesh.Triangles[triangle + 1];
					int ic = mesh.Triangles[triangle + 2];
					if (!TryAddSource(parents[piece], true, ToFloat3(mesh.Vertices[ia]), ToFloat3(mesh.Vertices[ib]), ToFloat3(mesh.Vertices[ic]), mesh.Uvs[ia], mesh.Uvs[ib], mesh.Uvs[ic], mesh.TerrainOutOfBounds, ref sourceOrder, sources, out failure))
					{
						failure += "-Heightfield-Piece-" + piece + "-" + triangle / 3;
						return false;
					}
				}
				reportProgress?.Invoke();
			}

			if (!TryGetProfileEndpoints(snapshot.Pieces, true, out int firstProfile, out int secondProfile, out failure))
				return false;
			for (int junctionIndex = 0; junctionIndex < snapshot.Junctions.Length; junctionIndex++)
			{
				SweepNetworkJunction junction = snapshot.Junctions[junctionIndex];
				for (int armIndex = 0; armIndex < junction.Arms.Length; armIndex++)
				{
					ct.ThrowIfCancellationRequested();
					SweepNetworkArm arm = junction.Arms[armIndex];
					List<SweepRibbonSourceTriangle> sources = GetList(result, parents[arm.PieceIndex]);
					if (!TryAddApproach(snapshot, arm, parents[arm.PieceIndex], firstProfile, secondProfile, true, ref sourceOrder, sources, out failure))
					{
						failure += "-Heightfield-Junction-" + junctionIndex + "-Arm-" + armIndex;
						return false;
					}
					reportProgress?.Invoke();
				}
			}
			return true;
		}

		private static bool TryAddApproach(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, int component, int firstProfile, int secondProfile, bool heightfield, ref int sourceOrder, List<SweepRibbonSourceTriangle> sources, out string failure)
		{
			failure = null;
			int sampleCount = arm.ApproachFrames?.Length ?? 0;
			if (sampleCount < 2 || arm.ApproachRights == null || arm.ApproachUps == null || arm.ApproachRights.Length != sampleCount || arm.ApproachUps.Length != sampleCount)
			{
				failure = "GlobalApproachInvalid";
				return false;
			}
			Vector3[] captured = CapturedRing(snapshot, arm);
			if (arm.Role == SweepNetworkArmRole.StripSeam && captured == null)
			{
				failure = "GlobalApproachPortalMissing";
				return false;
			}
			if (captured != null && (firstProfile >= captured.Length || secondProfile >= captured.Length))
			{
				failure = "GlobalApproachPortalInvalid";
				return false;
			}
			float portalDirection = ResolvePortalDirection(snapshot, arm, captured, firstProfile, secondProfile, sampleCount - 1);
			float[] directions = TransportDirections(arm, portalDirection);
			if (heightfield)
				return TryAddHeightfieldApproach(snapshot, arm, component, captured, directions, ref sourceOrder, sources, out failure);
			var firstWorld = new float3[sampleCount];
			var secondWorld = new float3[sampleCount];
			bool terrainOutOfBounds = false;
			for (int sample = 0; sample < sampleCount; sample++)
			{
				BuildSample(snapshot.Pieces, arm.ApproachFrames[sample], arm.ApproachRights[sample], arm.ApproachUps[sample], snapshot.Pieces.ProfilePoints[firstProfile], directions[sample], out float3 first, out float firstVertical);
				BuildSample(snapshot.Pieces, arm.ApproachFrames[sample], arm.ApproachRights[sample], arm.ApproachUps[sample], snapshot.Pieces.ProfilePoints[secondProfile], directions[sample], out float3 second, out float secondVertical);
				firstWorld[sample] = Drape(snapshot, first, firstVertical, ref terrainOutOfBounds);
				secondWorld[sample] = Drape(snapshot, second, secondVertical, ref terrainOutOfBounds);
			}
			if (captured != null)
			{
				firstWorld[sampleCount - 1] = ToFloat3(captured[firstProfile]);
				secondWorld[sampleCount - 1] = ToFloat3(captured[secondProfile]);
			}

			for (int sample = 0; sample < sampleCount - 1; sample++)
			{
				float3 a = firstWorld[sample];
				float3 b = firstWorld[sample + 1];
				float3 c = secondWorld[sample];
				float3 d = secondWorld[sample + 1];
				float2 uvA = PlanarUv(a, snapshot.UvScale);
				float2 uvB = PlanarUv(b, snapshot.UvScale);
				float2 uvC = PlanarUv(c, snapshot.UvScale);
				float2 uvD = PlanarUv(d, snapshot.UvScale);
				if (!TryAddSource(component, false, a, b, c, uvA, uvB, uvC, terrainOutOfBounds, ref sourceOrder, sources, out failure) || !TryAddSource(component, false, c, b, d, uvC, uvB, uvD, terrainOutOfBounds, ref sourceOrder, sources, out failure))
					return false;
			}
			return true;
		}

		private static bool TryAddHeightfieldApproach(
			SweepNetworkSnapshot snapshot,
			SweepNetworkArm arm,
			int component,
			Vector3[] captured,
			float[] directions,
			ref int sourceOrder,
			List<SweepRibbonSourceTriangle> sources,
			out string failure)
		{
			failure = null;
			SweepSnapshot pieces = snapshot.Pieces;
			int pointCount = pieces.ProfilePoints.Length;
			int sampleCount = arm.ApproachFrames.Length;
			if (captured != null && captured.Length < pointCount)
			{
				failure = "GlobalHeightfieldPortalInvalid";
				return false;
			}
			var positions = new float3[pointCount, sampleCount];
			bool terrainOutOfBounds = false;
			for (int point = 0; point < pointCount; point++)
			{
				for (int sample = 0; sample < sampleCount; sample++)
				{
					BuildSample(pieces, arm.ApproachFrames[sample], arm.ApproachRights[sample], arm.ApproachUps[sample], pieces.ProfilePoints[point], directions[sample], out float3 position, out float vertical);
					positions[point, sample] = Drape(snapshot, position, vertical, ref terrainOutOfBounds);
				}
				if (captured != null)
					positions[point, sampleCount - 1] = ToFloat3(captured[point]);
			}

			for (int sample = 0; sample < sampleCount - 1; sample++)
			{
				float v0 = arm.ApproachFrames[sample].Distance * snapshot.UvScale;
				float v1 = arm.ApproachFrames[sample + 1].Distance * snapshot.UvScale;
				for (int edge = 0; edge < pieces.ProfileSegments.Length; edge += 2)
				{
					int first = pieces.ProfileSegments[edge];
					int second = pieces.ProfileSegments[edge + 1];
					float3 a = positions[first, sample];
					float3 b = positions[first, sample + 1];
					float3 c = positions[second, sample];
					float3 d = positions[second, sample + 1];
					float2 uvA = new float2(pieces.ProfileUs[first], v0);
					float2 uvB = new float2(pieces.ProfileUs[first], v1);
					float2 uvC = new float2(pieces.ProfileUs[second], v0);
					float2 uvD = new float2(pieces.ProfileUs[second], v1);
					if (!TryAddSource(component, false, a, b, c, uvA, uvB, uvC, terrainOutOfBounds, ref sourceOrder, sources, out failure) || !TryAddSource(component, false, c, b, d, uvC, uvB, uvD, terrainOutOfBounds, ref sourceOrder, sources, out failure))
						return false;
				}
			}
			return true;
		}

		private static bool TryAddSource(int component, bool strip, float3 worldA, float3 worldB, float3 worldC, float2 uvA, float2 uvB, float2 uvC, bool terrainOutOfBounds, ref int sourceOrder, List<SweepRibbonSourceTriangle> sources, out string failure)
		{
			failure = null;
			float3 cross = math.cross(worldB - worldA, worldC - worldA);
			float area3D = math.length(cross);
			if (area3D <= MinimumTriangleArea)
				return true;
			float2 a = new float2(worldA.x, worldA.z);
			float2 b = new float2(worldB.x, worldB.z);
			float2 c = new float2(worldC.x, worldC.z);
			float areaPlan = Cross(b - a, c - a);
			if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || !math.all(math.isfinite(c)) || math.abs(areaPlan) / area3D < MinimumProjectionRatio)
			{
				failure = "GlobalProjectionFoldover";
				return false;
			}
			if (areaPlan < 0f)
			{
				Swap(ref b, ref c);
				Swap(ref worldB, ref worldC);
				Swap(ref uvB, ref uvC);
			}
			sources.Add(new SweepRibbonSourceTriangle
			{
				NetworkComponent = component,
				SourceOrder = sourceOrder++,
				Strip = strip,
				A = a,
				B = b,
				C = c,
				WorldA = worldA,
				WorldB = worldB,
				WorldC = worldC,
				UvA = uvA,
				UvB = uvB,
				UvC = uvC,
				TerrainOutOfBounds = terrainOutOfBounds
			});
			return true;
		}

		private static bool TryAddTerminal(int component, Vector3[] ring, int firstProfile, int secondProfile, List<SweepRibbonTerminalSegment> terminals, out string failure)
		{
			failure = null;
			if (ring == null || firstProfile >= ring.Length || secondProfile >= ring.Length)
			{
				failure = "GlobalTerminalRingMissing";
				return false;
			}
			float2 a = new float2(ring[firstProfile].x, ring[firstProfile].z);
			float2 b = new float2(ring[secondProfile].x, ring[secondProfile].z);
			if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || math.distancesq(a, b) <= 1e-12f)
			{
				failure = "GlobalTerminalInvalid";
				return false;
			}
			terminals.Add(new SweepRibbonTerminalSegment { NetworkComponent = component, A = a, B = b });
			return true;
		}

		private static SweepRibbonBoundaryKind[] ClassifyBoundary(float2[] ring, List<SweepRibbonTerminalSegment> terminals)
		{
			var result = new SweepRibbonBoundaryKind[ring.Length];
			float tolerance = (float)(3.0 / SweepRibbonPolygonUnion.Scale);
			for (int edge = 0; edge < ring.Length; edge++)
			{
				float2 a = ring[edge];
				float2 b = ring[(edge + 1) % ring.Length];
				for (int terminalIndex = 0; terminalIndex < terminals.Count; terminalIndex++)
				{
					SweepRibbonTerminalSegment terminal = terminals[terminalIndex];
					if (OnSegment(a, terminal.A, terminal.B, tolerance) && OnSegment(b, terminal.A, terminal.B, tolerance))
					{
						result[edge] = SweepRibbonBoundaryKind.Terminal;
						break;
					}
				}
			}
			return result;
		}

		private static bool OnSegment(float2 point, float2 a, float2 b, float tolerance)
		{
			float2 ab = b - a;
			float lengthSq = math.dot(ab, ab);
			if (lengthSq <= 1e-12f)
				return false;
			float raw = math.dot(point - a, ab) / lengthSq;
			float parameterTolerance = tolerance / math.sqrt(lengthSq);
			float2 projected = a + ab * math.saturate(raw);
			return raw >= -parameterTolerance && raw <= 1f + parameterTolerance && math.distancesq(point, projected) <= tolerance * tolerance;
		}

		private static Vector3[] CapturedRing(SweepNetworkSnapshot snapshot, SweepNetworkArm arm)
		{
			Vector3[][] rings = arm.AtPieceStart ? snapshot.PieceStartRings : snapshot.PieceEndRings;
			if (rings == null || arm.PieceIndex < 0 || arm.PieceIndex >= rings.Length)
				return null;
			return rings[arm.PieceIndex];
		}

		private static float ResolvePortalDirection(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, Vector3[] captured, int firstProfile, int secondProfile, int sample)
		{
			if (captured == null)
				return math.dot(arm.ApproachRights[sample], arm.Right) < 0f ? -1f : 1f;
			float direct = PortalError(snapshot, arm, captured, firstProfile, secondProfile, sample, 1f);
			float flipped = PortalError(snapshot, arm, captured, firstProfile, secondProfile, sample, -1f);
			return flipped + 1e-8f < direct ? -1f : 1f;
		}

		private static float PortalError(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, Vector3[] captured, int firstProfile, int secondProfile, int sample, float direction)
		{
			BuildSample(snapshot.Pieces, arm.ApproachFrames[sample], arm.ApproachRights[sample], arm.ApproachUps[sample], snapshot.Pieces.ProfilePoints[firstProfile], direction, out float3 first, out float firstVertical);
			BuildSample(snapshot.Pieces, arm.ApproachFrames[sample], arm.ApproachRights[sample], arm.ApproachUps[sample], snapshot.Pieces.ProfilePoints[secondProfile], direction, out float3 second, out float secondVertical);
			bool ignored = false;
			first = Drape(snapshot, first, firstVertical, ref ignored);
			second = Drape(snapshot, second, secondVertical, ref ignored);
			return math.distancesq(first, ToFloat3(captured[firstProfile])) + math.distancesq(second, ToFloat3(captured[secondProfile]));
		}

		private static float[] TransportDirections(SweepNetworkArm arm, float portalDirection)
		{
			int count = arm.ApproachFrames.Length;
			var directions = new float[count];
			directions[count - 1] = portalDirection;
			float3 nextRight = arm.ApproachRights[count - 1] * portalDirection;
			float3 nextUp = arm.ApproachUps[count - 1] * portalDirection;
			for (int sample = count - 2; sample >= 0; sample--)
			{
				float alignment = math.dot(arm.ApproachRights[sample], nextRight) + math.dot(arm.ApproachUps[sample], nextUp);
				float direction = alignment < 0f ? -1f : 1f;
				directions[sample] = direction;
				nextRight = arm.ApproachRights[sample] * direction;
				nextUp = arm.ApproachUps[sample] * direction;
			}
			return directions;
		}

		private static void BuildSample(SweepSnapshot pieces, SweepFrame frame, float3 right, float3 up, float2 profilePoint, float direction, out float3 position, out float vertical)
		{
			float width = SweepJunctionMeshBuilder.SampleLut(pieces.WidthLut, frame.T);
			float height = SweepJunctionMeshBuilder.SampleLut(pieces.HeightLut, frame.T);
			float twist = math.radians(SweepJunctionMeshBuilder.SampleLut(pieces.TwistLut, frame.T));
			float lateral = profilePoint.x * width;
			float profileVertical = profilePoint.y * height;
			float cosine = math.cos(twist);
			float sine = math.sin(twist);
			float rotatedLateral = lateral * cosine - profileVertical * sine;
			float rotatedVertical = lateral * sine + profileVertical * cosine;
			SweepJunctionMeshBuilder.MakeVertex(pieces.Terrain != null, frame.Position, right * direction, up * direction, rotatedLateral, rotatedVertical, out position, out vertical);
		}

		private static float3 Drape(SweepNetworkSnapshot snapshot, float3 position, float vertical, ref bool terrainOutOfBounds)
		{
			if (snapshot.Pieces.Terrain == null)
				return position;
			if (snapshot.Pieces.Terrain.TrySampleHeight(position.x, position.z, out float terrainHeight))
			{
				position.y = terrainHeight + snapshot.HeightOffset + vertical;
				return position;
			}
			terrainOutOfBounds = true;
			return position;
		}

		private static bool ProjectionValid(float3 a, float3 b, float3 c)
		{
			float area3D = math.length(math.cross(b - a, c - a));
			if (area3D <= MinimumTriangleArea)
				return true;
			float areaPlan = math.abs(Cross(new float2(b.x - a.x, b.z - a.z), new float2(c.x - a.x, c.z - a.z)));
			return areaPlan / area3D >= MinimumProjectionRatio;
		}

		private static bool TryGetProfileEndpoints(SweepSnapshot pieces, bool heightfield, out int first, out int second, out string failure)
		{
			first = -1;
			second = -1;
			failure = null;
			if (pieces.ProfileClosed || pieces.ProfilePoints == null || pieces.ProfileUs == null || pieces.ProfileSegments == null || pieces.ProfileSegments.Length < 2 || pieces.ProfileSegments.Length % 2 != 0 || pieces.ProfileUs.Length != pieces.ProfilePoints.Length)
			{
				failure = heightfield ? "GlobalHeightfieldProfileRequired" : "GlobalRibbonProfileRequired";
				return false;
			}
			if (!heightfield)
			{
				if (pieces.ProfilePoints.Length != 2 || pieces.ProfileSegments.Length != 2)
				{
					failure = "GlobalRibbonProfileRequired";
					return false;
				}
				first = pieces.ProfileSegments[0];
				second = pieces.ProfileSegments[1];
				if (first < 0 || second < 0 || first >= 2 || second >= 2 || first == second)
				{
					failure = "GlobalRibbonSegmentInvalid";
					return false;
				}
				return true;
			}

			if (pieces.ProfilePoints.Length < 3 || pieces.ProfileSegments.Length / 2 != pieces.ProfilePoints.Length - 1 || !ValidateScaleLut(pieces.WidthLut) || !ValidateScaleLut(pieces.HeightLut) || !ValidateZeroLut(pieces.TwistLut))
			{
				failure = "GlobalHeightfieldProfileInvalid";
				return false;
			}
			float scale = 1f;
			for (int point = 0; point < pieces.ProfilePoints.Length; point++)
			{
				if (!math.all(math.isfinite(pieces.ProfilePoints[point])) || !math.isfinite(pieces.ProfileUs[point]))
				{
					failure = "GlobalHeightfieldProfileInvalid";
					return false;
				}
				scale = math.max(scale, math.cmax(math.abs(pieces.ProfilePoints[point])));
			}
			float tolerance = scale * 1e-5f;
			int direction = 0;
			first = pieces.ProfileSegments[0];
			int current = first;
			for (int edge = 0; edge < pieces.ProfileSegments.Length; edge += 2)
			{
				int a = pieces.ProfileSegments[edge];
				int b = pieces.ProfileSegments[edge + 1];
				if (a != current || a < 0 || b < 0 || a >= pieces.ProfilePoints.Length || b >= pieces.ProfilePoints.Length || a == b)
				{
					failure = "GlobalHeightfieldProfileDisconnected";
					return false;
				}
				float delta = pieces.ProfilePoints[b].x - pieces.ProfilePoints[a].x;
				int edgeDirection = delta > tolerance ? 1 : delta < -tolerance ? -1 : 0;
				if (edgeDirection == 0 || direction != 0 && edgeDirection != direction)
				{
					failure = "GlobalHeightfieldProfileNotMonotone";
					return false;
				}
				direction = edgeDirection;
				current = b;
			}
			second = current;
			if (math.abs(pieces.ProfilePoints[first].y - pieces.ProfilePoints[second].y) > tolerance || math.abs(ProfileSignedArea(pieces, first, second)) <= tolerance * tolerance)
			{
				failure = "GlobalHeightfieldBoundaryInvalid";
				return false;
			}
			return true;
		}

		private static float ProfileSignedArea(SweepSnapshot pieces, int first, int second)
		{
			float area = 0f;
			for (int edge = 0; edge < pieces.ProfileSegments.Length; edge += 2)
			{
				float2 a = pieces.ProfilePoints[pieces.ProfileSegments[edge]];
				float2 b = pieces.ProfilePoints[pieces.ProfileSegments[edge + 1]];
				area += a.x * b.y - b.x * a.y;
			}
			float2 end = pieces.ProfilePoints[second];
			float2 start = pieces.ProfilePoints[first];
			return (area + end.x * start.y - start.x * end.y) * 0.5f;
		}

		private static bool ValidateScaleLut(float[] values)
		{
			if (values == null || values.Length == 0)
				return false;
			for (int index = 0; index < values.Length; index++)
			{
				if (!math.isfinite(values[index]) || values[index] <= 1e-5f)
					return false;
			}
			return true;
		}

		private static bool ValidateZeroLut(float[] values)
		{
			if (values == null || values.Length == 0)
				return false;
			for (int index = 0; index < values.Length; index++)
			{
				if (!math.isfinite(values[index]) || math.abs(values[index]) > 1e-4f)
					return false;
			}
			return true;
		}

		private static List<T> GetList<T>(SortedDictionary<int, List<T>> dictionary, int key)
		{
			if (!dictionary.TryGetValue(key, out List<T> result))
			{
				result = new List<T>();
				dictionary.Add(key, result);
			}
			return result;
		}

		private static int Root(int[] parents, int value)
		{
			int root = value;
			while (parents[root] != root)
				root = parents[root];
			while (parents[value] != value)
			{
				int next = parents[value];
				parents[value] = root;
				value = next;
			}
			return root;
		}

		private static void Union(int[] parents, int first, int second)
		{
			int firstRoot = Root(parents, first);
			int secondRoot = Root(parents, second);
			if (firstRoot == secondRoot)
				return;
			if (firstRoot < secondRoot)
				parents[secondRoot] = firstRoot;
			else
				parents[firstRoot] = secondRoot;
		}

		private static float2 PlanarUv(float3 position, float scale)
		{
			return new float2(position.x * scale, position.z * scale);
		}

		private static float Cross(float2 first, float2 second)
		{
			return first.x * second.y - first.y * second.x;
		}

		private static void Swap(ref float2 first, ref float2 second)
		{
			float2 value = first;
			first = second;
			second = value;
		}

		private static void Swap(ref float3 first, ref float3 second)
		{
			float3 value = first;
			first = second;
			second = value;
		}

		private static float3 ToFloat3(Vector3 value)
		{
			return new float3(value.x, value.y, value.z);
		}
	}
}
