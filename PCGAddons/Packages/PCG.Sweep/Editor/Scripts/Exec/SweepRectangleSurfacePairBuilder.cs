using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRectangleSurfacePairBuilder
	{
		private const float MinimumProjectionRatio = 0.05f;
		private const float MinimumTriangleArea = 1e-10f;

		internal static bool TryBuild(
			SweepNetworkSnapshot snapshot,
			SweepRectangleProfileInfo profile,
			CancellationToken ct,
			Action reportProgress,
			out SweepRibbonNetworkDomain domain,
			out SweepRectangleSourceTriangle[] sources,
			out string failure)
		{
			domain = null;
			sources = null;
			failure = null;
			if (snapshot == null || snapshot.Pieces == null || snapshot.Pieces.Frames == null || snapshot.Junctions == null)
			{
				failure = "RectangleNetworkDataMissing";
				return false;
			}

			Action progress = reportProgress ?? (() => { });
			SweepSnapshot bottomPieces = BuildSurfaceSnapshot(snapshot.Pieces, profile.BottomPoints, profile.BottomUs);
			SweepSnapshot topPieces = BuildSurfaceSnapshot(snapshot.Pieces, profile.TopPoints, profile.TopUs);
			SweepMeshData[] bottomMeshes = BuildPieceMeshes(bottomPieces, ct, progress);
			SweepMeshData[] topMeshes = BuildPieceMeshes(topPieces, ct, progress);
			if (!ValidatePairedMeshes(bottomMeshes, topMeshes, out failure))
				return false;

			SweepNetworkSnapshot bottomNetwork = BuildNetworkSnapshot(snapshot, bottomPieces, bottomMeshes);
			SweepNetworkSnapshot topNetwork = BuildNetworkSnapshot(snapshot, topPieces, topMeshes);
			if (!SweepRibbonNetworkDomainBuilder.TryBuild(bottomNetwork, ct, progress, out domain, out failure))
			{
				failure = "RectangleBottom-" + failure;
				return false;
			}

			if (!TryBuildSources(bottomNetwork, topNetwork, ct, progress, out sources, out failure))
				return false;
			if (!ValidateDomainSources(domain, sources, out failure))
				return false;
			return true;
		}

		private static SweepSnapshot BuildSurfaceSnapshot(SweepSnapshot source, float2[] points, float[] us)
		{
			return new SweepSnapshot
			{
				ProfilePoints = (float2[])points.Clone(),
				ProfileUs = (float[])us.Clone(),
				ProfileSegments = new[] { 0, 1 },
				ProfileClosed = false,
				Frames = source.Frames,
				SplineClosed = source.SplineClosed,
				WidthLut = source.WidthLut,
				HeightLut = source.HeightLut,
				TwistLut = source.TwistLut,
				Terrain = source.Terrain,
				MaxLateralExtent = source.MaxLateralExtent,
				UvScale = source.UvScale,
				HeightOffset = source.HeightOffset,
				CapStartFlags = source.CapStartFlags,
				CapEndFlags = source.CapEndFlags,
				Collider = source.Collider,
				Name = source.Name
			};
		}

		private static SweepMeshData[] BuildPieceMeshes(SweepSnapshot pieces, CancellationToken ct, Action reportProgress)
		{
			var meshes = new SweepMeshData[pieces.Frames.Length];
			for (int piece = 0; piece < pieces.Frames.Length; piece++)
			{
				ct.ThrowIfCancellationRequested();
				if (pieces.Frames[piece] != null)
					meshes[piece] = SweepMeshBuilder.Build(pieces, piece, ct, reportProgress);
				reportProgress();
			}
			return meshes;
		}

		private static SweepNetworkSnapshot BuildNetworkSnapshot(SweepNetworkSnapshot source, SweepSnapshot pieces, SweepMeshData[] meshes)
		{
			var startRings = new Vector3[meshes.Length][];
			var endRings = new Vector3[meshes.Length][];
			for (int piece = 0; piece < meshes.Length; piece++)
			{
				startRings[piece] = meshes[piece].StartRing;
				endRings[piece] = meshes[piece].EndRing;
			}
			return new SweepNetworkSnapshot
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
		}

		private static bool ValidatePairedMeshes(SweepMeshData[] bottom, SweepMeshData[] top, out string failure)
		{
			failure = null;
			if (bottom.Length != top.Length)
			{
				failure = "RectangleSurfacePieceCountMismatch";
				return false;
			}
			for (int piece = 0; piece < bottom.Length; piece++)
			{
				bool bottomEmpty = bottom[piece].Vertices == null;
				bool topEmpty = top[piece].Vertices == null;
				if (bottomEmpty != topEmpty)
				{
					failure = "RectangleSurfacePresenceMismatch-" + piece;
					return false;
				}
				if (bottomEmpty)
					continue;
				if (bottom[piece].Vertices.Length != top[piece].Vertices.Length || bottom[piece].Uvs.Length != bottom[piece].Vertices.Length || top[piece].Uvs.Length != top[piece].Vertices.Length || bottom[piece].Triangles.Length != top[piece].Triangles.Length)
				{
					failure = "RectangleSurfaceTopologyMismatch-" + piece;
					return false;
				}
				for (int index = 0; index < bottom[piece].Triangles.Length; index++)
				{
					if (bottom[piece].Triangles[index] != top[piece].Triangles[index])
					{
						failure = "RectangleSurfaceIndexMismatch-" + piece + "-" + index;
						return false;
					}
				}
				if (bottom[piece].StartRing == null || top[piece].StartRing == null || bottom[piece].EndRing == null || top[piece].EndRing == null || bottom[piece].StartRing.Length != 2 || top[piece].StartRing.Length != 2 || bottom[piece].EndRing.Length != 2 || top[piece].EndRing.Length != 2)
				{
					failure = "RectangleSurfaceRingMismatch-" + piece;
					return false;
				}
			}
			return true;
		}

		private static bool TryBuildSources(
			SweepNetworkSnapshot bottom,
			SweepNetworkSnapshot top,
			CancellationToken ct,
			Action reportProgress,
			out SweepRectangleSourceTriangle[] result,
			out string failure)
		{
			result = null;
			failure = null;
			if (!TryBuildComponentRoots(bottom, out int[] roots, out failure))
				return false;

			var sources = new List<SweepRectangleSourceTriangle>();
			for (int piece = 0; piece < bottom.PieceMeshes.Length; piece++)
			{
				ct.ThrowIfCancellationRequested();
				SweepMeshData bottomMesh = bottom.PieceMeshes[piece];
				SweepMeshData topMesh = top.PieceMeshes[piece];
				if (bottomMesh.Vertices == null)
					continue;
				for (int triangle = 0; triangle < bottomMesh.Triangles.Length; triangle += 3)
				{
					int ia = bottomMesh.Triangles[triangle];
					int ib = bottomMesh.Triangles[triangle + 1];
					int ic = bottomMesh.Triangles[triangle + 2];
					if (!TryAddSource(
						roots[piece],
						true,
						ToFloat3(bottomMesh.Vertices[ia]),
						ToFloat3(bottomMesh.Vertices[ib]),
						ToFloat3(bottomMesh.Vertices[ic]),
						ToFloat3(topMesh.Vertices[ia]),
						ToFloat3(topMesh.Vertices[ib]),
						ToFloat3(topMesh.Vertices[ic]),
						bottomMesh.Uvs[ia],
						bottomMesh.Uvs[ib],
						bottomMesh.Uvs[ic],
						topMesh.Uvs[ia],
						topMesh.Uvs[ib],
						topMesh.Uvs[ic],
						bottomMesh.TerrainOutOfBounds || topMesh.TerrainOutOfBounds,
						sources,
						out failure))
					{
						failure += "-Piece-" + piece + "-" + triangle / 3;
						return false;
					}
				}
				reportProgress();
			}

			for (int junctionIndex = 0; junctionIndex < bottom.Junctions.Length; junctionIndex++)
			{
				SweepNetworkJunction junction = bottom.Junctions[junctionIndex];
				for (int armIndex = 0; armIndex < junction.Arms.Length; armIndex++)
				{
					ct.ThrowIfCancellationRequested();
					SweepNetworkArm arm = junction.Arms[armIndex];
					if (!TryAddApproach(bottom, top, arm, roots[arm.PieceIndex], sources, out failure))
					{
						failure += "-Junction-" + junctionIndex + "-Arm-" + armIndex;
						return false;
					}
					reportProgress();
				}
			}

			result = sources.ToArray();
			return true;
		}

		private static bool TryBuildComponentRoots(SweepNetworkSnapshot snapshot, out int[] roots, out string failure)
		{
			failure = null;
			int pieceCount = snapshot.Pieces.Frames.Length;
			roots = new int[pieceCount];
			for (int piece = 0; piece < pieceCount; piece++)
				roots[piece] = piece;
			for (int junctionIndex = 0; junctionIndex < snapshot.Junctions.Length; junctionIndex++)
			{
				SweepNetworkJunction junction = snapshot.Junctions[junctionIndex];
				if (junction == null || junction.Arms == null)
				{
					failure = "RectangleJunctionInvalid-" + junctionIndex;
					return false;
				}
				int firstPiece = -1;
				for (int armIndex = 0; armIndex < junction.Arms.Length; armIndex++)
				{
					SweepNetworkArm arm = junction.Arms[armIndex];
					if (arm == null || arm.PieceIndex < 0 || arm.PieceIndex >= pieceCount)
					{
						failure = "RectangleArmInvalid-" + junctionIndex + "-" + armIndex;
						return false;
					}
					if (firstPiece < 0)
						firstPiece = arm.PieceIndex;
					else
						Union(roots, firstPiece, arm.PieceIndex);
				}
			}
			for (int piece = 0; piece < roots.Length; piece++)
				roots[piece] = Root(roots, piece);
			return true;
		}

		private static bool TryAddApproach(
			SweepNetworkSnapshot bottom,
			SweepNetworkSnapshot top,
			SweepNetworkArm arm,
			int component,
			List<SweepRectangleSourceTriangle> sources,
			out string failure)
		{
			failure = null;
			int sampleCount = arm.ApproachFrames?.Length ?? 0;
			if (sampleCount < 2 || arm.ApproachRights == null || arm.ApproachUps == null || arm.ApproachRights.Length != sampleCount || arm.ApproachUps.Length != sampleCount)
			{
				failure = "RectangleApproachInvalid";
				return false;
			}

			Vector3[] bottomCaptured = CapturedRing(bottom, arm);
			Vector3[] topCaptured = CapturedRing(top, arm);
			if (arm.Role == SweepNetworkArmRole.StripSeam && (bottomCaptured == null || topCaptured == null))
			{
				failure = "RectangleApproachPortalMissing";
				return false;
			}
			if (bottomCaptured != null && (bottomCaptured.Length != 2 || topCaptured == null || topCaptured.Length != 2))
			{
				failure = "RectangleApproachPortalInvalid";
				return false;
			}

			float portalDirection = ResolvePortalDirection(bottom, arm, bottomCaptured, sampleCount - 1);
			float[] directions = TransportDirections(arm, portalDirection);
			var bottomFirst = new float3[sampleCount];
			var bottomSecond = new float3[sampleCount];
			var topFirst = new float3[sampleCount];
			var topSecond = new float3[sampleCount];
			bool terrainOutOfBounds = false;
			for (int sample = 0; sample < sampleCount; sample++)
			{
				BuildSample(bottom.Pieces, arm, sample, bottom.Pieces.ProfilePoints[0], directions[sample], out float3 bottomA, out float bottomVerticalA);
				BuildSample(bottom.Pieces, arm, sample, bottom.Pieces.ProfilePoints[1], directions[sample], out float3 bottomB, out float bottomVerticalB);
				BuildSample(top.Pieces, arm, sample, top.Pieces.ProfilePoints[0], directions[sample], out float3 topA, out float topVerticalA);
				BuildSample(top.Pieces, arm, sample, top.Pieces.ProfilePoints[1], directions[sample], out float3 topB, out float topVerticalB);
				bottomFirst[sample] = Drape(bottom, bottomA, bottomVerticalA, ref terrainOutOfBounds);
				bottomSecond[sample] = Drape(bottom, bottomB, bottomVerticalB, ref terrainOutOfBounds);
				topFirst[sample] = Drape(top, topA, topVerticalA, ref terrainOutOfBounds);
				topSecond[sample] = Drape(top, topB, topVerticalB, ref terrainOutOfBounds);
			}
			if (bottomCaptured != null)
			{
				bottomFirst[sampleCount - 1] = ToFloat3(bottomCaptured[0]);
				bottomSecond[sampleCount - 1] = ToFloat3(bottomCaptured[1]);
				topFirst[sampleCount - 1] = ToFloat3(topCaptured[0]);
				topSecond[sampleCount - 1] = ToFloat3(topCaptured[1]);
			}

			for (int sample = 0; sample < sampleCount - 1; sample++)
			{
				float3 bottomA = bottomFirst[sample];
				float3 bottomB = bottomFirst[sample + 1];
				float3 bottomC = bottomSecond[sample];
				float3 bottomD = bottomSecond[sample + 1];
				float3 topA = topFirst[sample];
				float3 topB = topFirst[sample + 1];
				float3 topC = topSecond[sample];
				float3 topD = topSecond[sample + 1];
				float2 bottomUvA = PlanarUv(bottomA, bottom.UvScale);
				float2 bottomUvB = PlanarUv(bottomB, bottom.UvScale);
				float2 bottomUvC = PlanarUv(bottomC, bottom.UvScale);
				float2 bottomUvD = PlanarUv(bottomD, bottom.UvScale);
				float2 topUvA = PlanarUv(topA, top.UvScale);
				float2 topUvB = PlanarUv(topB, top.UvScale);
				float2 topUvC = PlanarUv(topC, top.UvScale);
				float2 topUvD = PlanarUv(topD, top.UvScale);
				if (!TryAddSource(component, false, bottomA, bottomB, bottomC, topA, topB, topC, bottomUvA, bottomUvB, bottomUvC, topUvA, topUvB, topUvC, terrainOutOfBounds, sources, out failure))
					return false;
				if (!TryAddSource(component, false, bottomC, bottomB, bottomD, topC, topB, topD, bottomUvC, bottomUvB, bottomUvD, topUvC, topUvB, topUvD, terrainOutOfBounds, sources, out failure))
					return false;
			}
			return true;
		}

		private static bool TryAddSource(
			int component,
			bool strip,
			float3 bottomA,
			float3 bottomB,
			float3 bottomC,
			float3 topA,
			float3 topB,
			float3 topC,
			float2 bottomUvA,
			float2 bottomUvB,
			float2 bottomUvC,
			float2 topUvA,
			float2 topUvB,
			float2 topUvC,
			bool terrainOutOfBounds,
			List<SweepRectangleSourceTriangle> sources,
			out string failure)
		{
			failure = null;
			float bottomArea3D = math.length(math.cross(bottomB - bottomA, bottomC - bottomA));
			if (bottomArea3D <= MinimumTriangleArea)
				return true;
			float2 a = new float2(bottomA.x, bottomA.z);
			float2 b = new float2(bottomB.x, bottomB.z);
			float2 c = new float2(bottomC.x, bottomC.z);
			float planArea = Cross(b - a, c - a);
			if (!Finite(bottomA) || !Finite(bottomB) || !Finite(bottomC) || !Finite(topA) || !Finite(topB) || !Finite(topC) || math.abs(planArea) / bottomArea3D < MinimumProjectionRatio)
			{
				failure = "RectangleProjectionFoldover";
				return false;
			}
			if (math.lengthsq(math.cross(topB - topA, topC - topA)) <= MinimumTriangleArea * MinimumTriangleArea)
			{
				failure = "RectangleTopDegenerate";
				return false;
			}
			if (!Verticalize(bottomA, ref topA) || !Verticalize(bottomB, ref topB) || !Verticalize(bottomC, ref topC))
			{
				failure = "RectangleThicknessInvalid";
				return false;
			}
			if (planArea < 0f)
			{
				Swap(ref b, ref c);
				Swap(ref bottomB, ref bottomC);
				Swap(ref topB, ref topC);
				Swap(ref bottomUvB, ref bottomUvC);
				Swap(ref topUvB, ref topUvC);
			}

			sources.Add(new SweepRectangleSourceTriangle
			{
				NetworkComponent = component,
				SourceOrder = sources.Count,
				Strip = strip,
				A = a,
				B = b,
				C = c,
				BottomA = bottomA,
				BottomB = bottomB,
				BottomC = bottomC,
				TopA = topA,
				TopB = topB,
				TopC = topC,
				BottomUvA = bottomUvA,
				BottomUvB = bottomUvB,
				BottomUvC = bottomUvC,
				TopUvA = topUvA,
				TopUvB = topUvB,
				TopUvC = topUvC,
				TerrainOutOfBounds = terrainOutOfBounds
			});
			return true;
		}

		private static bool Verticalize(float3 bottom, ref float3 top)
		{
			float thickness = math.distance(bottom, top);
			if (!math.isfinite(thickness) || thickness <= 1e-5f)
				return false;
			top = new float3(bottom.x, bottom.y + thickness, bottom.z);
			return true;
		}

		private static bool ValidateDomainSources(SweepRibbonNetworkDomain domain, SweepRectangleSourceTriangle[] sources, out string failure)
		{
			failure = null;
			var referenced = new bool[sources.Length];
			float tolerance = (float)(2.0 / SweepRibbonPolygonUnion.Scale);
			for (int componentIndex = 0; componentIndex < domain.Components.Length; componentIndex++)
			{
				SweepRibbonSourceTriangle[] bottomSources = domain.Components[componentIndex].Sources;
				for (int sourceIndex = 0; sourceIndex < bottomSources.Length; sourceIndex++)
				{
					SweepRibbonSourceTriangle bottom = bottomSources[sourceIndex];
					if (bottom.SourceOrder < 0 || bottom.SourceOrder >= sources.Length)
					{
						failure = "RectangleSourceOrderInvalid-" + bottom.SourceOrder;
						return false;
					}
					SweepRectangleSourceTriangle pair = sources[bottom.SourceOrder];
					if (pair.SourceOrder != bottom.SourceOrder || pair.NetworkComponent != bottom.NetworkComponent || pair.Strip != bottom.Strip || math.distance(pair.A, bottom.A) > tolerance || math.distance(pair.B, bottom.B) > tolerance || math.distance(pair.C, bottom.C) > tolerance)
					{
						failure = "RectangleSourceMismatch-" + bottom.SourceOrder;
						return false;
					}
					referenced[bottom.SourceOrder] = true;
				}
			}
			for (int source = 0; source < referenced.Length; source++)
			{
				if (!referenced[source])
				{
					failure = "RectangleSourceUnreferenced-" + source;
					return false;
				}
			}
			return true;
		}

		private static Vector3[] CapturedRing(SweepNetworkSnapshot snapshot, SweepNetworkArm arm)
		{
			Vector3[][] rings = arm.AtPieceStart ? snapshot.PieceStartRings : snapshot.PieceEndRings;
			if (rings == null || arm.PieceIndex < 0 || arm.PieceIndex >= rings.Length)
				return null;
			return rings[arm.PieceIndex];
		}

		private static float ResolvePortalDirection(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, Vector3[] captured, int sample)
		{
			if (captured == null)
				return math.dot(arm.ApproachRights[sample], arm.Right) < 0f ? -1f : 1f;
			float direct = PortalError(snapshot, arm, captured, sample, 1f);
			float flipped = PortalError(snapshot, arm, captured, sample, -1f);
			return flipped + 1e-8f < direct ? -1f : 1f;
		}

		private static float PortalError(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, Vector3[] captured, int sample, float direction)
		{
			BuildSample(snapshot.Pieces, arm, sample, snapshot.Pieces.ProfilePoints[0], direction, out float3 first, out float firstVertical);
			BuildSample(snapshot.Pieces, arm, sample, snapshot.Pieces.ProfilePoints[1], direction, out float3 second, out float secondVertical);
			bool ignored = false;
			first = Drape(snapshot, first, firstVertical, ref ignored);
			second = Drape(snapshot, second, secondVertical, ref ignored);
			return math.distancesq(first, ToFloat3(captured[0])) + math.distancesq(second, ToFloat3(captured[1]));
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

		private static void BuildSample(SweepSnapshot pieces, SweepNetworkArm arm, int sample, float2 profilePoint, float direction, out float3 position, out float vertical)
		{
			SweepFrame frame = arm.ApproachFrames[sample];
			float width = SweepJunctionMeshBuilder.SampleLut(pieces.WidthLut, frame.T);
			float height = SweepJunctionMeshBuilder.SampleLut(pieces.HeightLut, frame.T);
			float twist = math.radians(SweepJunctionMeshBuilder.SampleLut(pieces.TwistLut, frame.T));
			float lateral = profilePoint.x * width;
			float profileVertical = profilePoint.y * height;
			float cosine = math.cos(twist);
			float sine = math.sin(twist);
			float rotatedLateral = lateral * cosine - profileVertical * sine;
			float rotatedVertical = lateral * sine + profileVertical * cosine;
			SweepJunctionMeshBuilder.MakeVertex(pieces.Terrain != null, frame.Position, arm.ApproachRights[sample] * direction, arm.ApproachUps[sample] * direction, rotatedLateral, rotatedVertical, out position, out vertical);
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

		private static bool Finite(float3 value)
		{
			return math.all(math.isfinite(value));
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
