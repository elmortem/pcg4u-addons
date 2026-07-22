using System;
using System.Collections.Generic;
using System.Threading;
using PCG.Polygons;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.Sweep
{
	internal static class SweepRibbonJunctionMeshBuilder
	{
		internal static bool CanBuild(SweepNetworkSnapshot snapshot)
		{
			if (snapshot == null || snapshot.Pieces == null)
				return false;

			SweepSnapshot pieces = snapshot.Pieces;
			if (pieces.ProfileClosed || pieces.ProfilePoints == null || pieces.ProfilePoints.Length != 2 || pieces.ProfileSegments == null || pieces.ProfileSegments.Length != 2)
				return false;

			int first = pieces.ProfileSegments[0];
			int second = pieces.ProfileSegments[1];
			if (first < 0 || second < 0 || first >= pieces.ProfilePoints.Length || second >= pieces.ProfilePoints.Length || first == second)
				return false;

			return math.all(math.isfinite(pieces.ProfilePoints[first])) &&
				math.all(math.isfinite(pieces.ProfilePoints[second])) &&
				math.distancesq(pieces.ProfilePoints[first], pieces.ProfilePoints[second]) > 1e-12f;
		}

		internal static SweepMeshData Build(SweepNetworkSnapshot snapshot, int junctionIndex, CancellationToken ct, Action reportProgress)
		{
			if (!CanBuild(snapshot))
				return new SweepMeshData { FailureCode = "RibbonProfileInvalid" };
			if (snapshot.Junctions == null || junctionIndex < 0 || junctionIndex >= snapshot.Junctions.Length)
				return new SweepMeshData { FailureCode = "RibbonJunctionInvalid" };

			SweepSnapshot pieces = snapshot.Pieces;
			SweepNetworkJunction junction = snapshot.Junctions[junctionIndex];
			SweepNetworkArm[] arms = junction.Arms;
			if (arms == null || arms.Length == 0)
				return new SweepMeshData { FailureCode = "RibbonArmsMissing" };

			int firstProfile = pieces.ProfileSegments[0];
			int secondProfile = pieces.ProfileSegments[1];
			int armCount = arms.Length;
			float3 center = junction.Center;
			float3 axis = junction.Axis;
			float3 e1 = junction.E1;
			float3 e2 = junction.E2;
			float step = math.max(0.05f, snapshot.Step);
			bool hasTerrain = pieces.Terrain != null;

			var portalCw = new float2[armCount];
			var portalCcw = new float2[armCount];
			var portalCwWorld = new float3[armCount];
			var portalCcwWorld = new float3[armCount];
			var activePortal = new bool[armCount];
			var portalOwnership = new List<Polygon2D>[armCount];
			var corridorCw = new float2[armCount][];
			var corridorCcw = new float2[armCount][];
			var sourceA = new List<float2>();
			var sourceB = new List<float2>();
			var sourceHeightA = new List<float>();
			var sourceHeightB = new List<float>();
			var sourceVerticalA = new List<float>();
			var sourceVerticalB = new List<float>();
			float minPortalWidth = float.MaxValue;
			bool terrainOutOfBounds = false;

			for (int armIndex = 0; armIndex < armCount; armIndex++)
			{
				ct.ThrowIfCancellationRequested();
				SweepNetworkArm arm = arms[armIndex];
				if (arm == null)
					return new SweepMeshData { FailureCode = "RibbonArmInvalid-" + armIndex };

				int sampleCount = arm.ApproachFrames?.Length ?? 0;
				if (sampleCount < 2 || arm.ApproachRights == null || arm.ApproachUps == null || arm.ApproachRights.Length != sampleCount || arm.ApproachUps.Length != sampleCount)
					return new SweepMeshData { FailureCode = "RibbonApproachMissing-" + armIndex };

				Vector3[] capturedRing = CapturedRing(snapshot, arm);
				activePortal[armIndex] = arm.Role == SweepNetworkArmRole.StripSeam || arm.Role == SweepNetworkArmRole.PatchSeam;
				if (arm.Role == SweepNetworkArmRole.StripSeam && capturedRing == null)
					return new SweepMeshData { FailureCode = "RibbonStripPortalMissing-" + armIndex };
				if (arm.Role == SweepNetworkArmRole.StripSeam && !TryBuildStripOwnership(snapshot, arm, center, e1, e2, out portalOwnership[armIndex]))
					return new SweepMeshData { FailureCode = "RibbonStripFootprintMissing-" + armIndex };
				if (capturedRing != null && (firstProfile >= capturedRing.Length || secondProfile >= capturedRing.Length))
					return new SweepMeshData { FailureCode = "RibbonPortalSizeMismatch-" + armIndex };

				int lastSample = sampleCount - 1;
				float portalDirection = ResolvePortalDirection(snapshot, arm, capturedRing, firstProfile, secondProfile, lastSample);
				float[] basisDirections = TransportBasisDirections(arm, portalDirection);
				BuildSample(pieces, arm.ApproachFrames[lastSample], arm.ApproachRights[lastSample], arm.ApproachUps[lastSample], pieces.ProfilePoints[firstProfile], basisDirections[lastSample], hasTerrain, out float3 firstRawPortal, out float firstPortalVertical);
				BuildSample(pieces, arm.ApproachFrames[lastSample], arm.ApproachRights[lastSample], arm.ApproachUps[lastSample], pieces.ProfilePoints[secondProfile], basisDirections[lastSample], hasTerrain, out float3 secondRawPortal, out float secondPortalVertical);
				float3 firstWorldPortal = capturedRing == null ? Drape(snapshot, firstRawPortal, firstPortalVertical, ref terrainOutOfBounds) : ToFloat3(capturedRing[firstProfile]);
				float3 secondWorldPortal = capturedRing == null ? Drape(snapshot, secondRawPortal, secondPortalVertical, ref terrainOutOfBounds) : ToFloat3(capturedRing[secondProfile]);

				float2 firstPlan = Planar(firstWorldPortal - center, e1, e2);
				float2 secondPlan = Planar(secondWorldPortal - center, e1, e2);
				float2 outward = math.normalizesafe(Planar(arm.Outward, e1, e2));
				float orientation = Cross(outward, firstPlan - secondPlan);
				if (math.abs(orientation) < 1e-7f * math.max(1f, math.distance(firstPlan, secondPlan)))
					return new SweepMeshData { FailureCode = "RibbonPlanDegenerate-" + armIndex };

				int cwProfile;
				int ccwProfile;
				float3 cwRawPortal;
				float3 ccwRawPortal;
				float cwPortalVertical;
				float ccwPortalVertical;
				if (orientation > 0f)
				{
					ccwProfile = firstProfile;
					cwProfile = secondProfile;
					portalCcwWorld[armIndex] = firstWorldPortal;
					portalCwWorld[armIndex] = secondWorldPortal;
					ccwRawPortal = firstRawPortal;
					cwRawPortal = secondRawPortal;
					ccwPortalVertical = firstPortalVertical;
					cwPortalVertical = secondPortalVertical;
				}
				else
				{
					cwProfile = firstProfile;
					ccwProfile = secondProfile;
					portalCwWorld[armIndex] = firstWorldPortal;
					portalCcwWorld[armIndex] = secondWorldPortal;
					cwRawPortal = firstRawPortal;
					ccwRawPortal = secondRawPortal;
					cwPortalVertical = firstPortalVertical;
					ccwPortalVertical = secondPortalVertical;
				}

				portalCw[armIndex] = Planar(portalCwWorld[armIndex] - center, e1, e2);
				portalCcw[armIndex] = Planar(portalCcwWorld[armIndex] - center, e1, e2);
				minPortalWidth = math.min(minPortalWidth, math.distance(portalCw[armIndex], portalCcw[armIndex]));

				corridorCw[armIndex] = new float2[sampleCount];
				corridorCcw[armIndex] = new float2[sampleCount];
				var cwHeight = new float[sampleCount];
				var ccwHeight = new float[sampleCount];
				var cwVertical = new float[sampleCount];
				var ccwVertical = new float[sampleCount];

				for (int sample = 0; sample < sampleCount; sample++)
				{
					SweepFrame frame = arm.ApproachFrames[sample];
					BuildSample(pieces, frame, arm.ApproachRights[sample], arm.ApproachUps[sample], pieces.ProfilePoints[cwProfile], basisDirections[sample], hasTerrain, out float3 cwPosition, out float cwRv);
					BuildSample(pieces, frame, arm.ApproachRights[sample], arm.ApproachUps[sample], pieces.ProfilePoints[ccwProfile], basisDirections[sample], hasTerrain, out float3 ccwPosition, out float ccwRv);
					corridorCw[armIndex][sample] = Planar(cwPosition - center, e1, e2);
					corridorCcw[armIndex][sample] = Planar(ccwPosition - center, e1, e2);
					cwHeight[sample] = math.dot(cwPosition - center, axis);
					ccwHeight[sample] = math.dot(ccwPosition - center, axis);
					cwVertical[sample] = cwRv;
					ccwVertical[sample] = ccwRv;
				}

				corridorCw[armIndex][lastSample] = portalCw[armIndex];
				corridorCcw[armIndex][lastSample] = portalCcw[armIndex];
				cwHeight[lastSample] = math.dot(cwRawPortal - center, axis);
				ccwHeight[lastSample] = math.dot(ccwRawPortal - center, axis);
				cwVertical[lastSample] = cwPortalVertical;
				ccwVertical[lastSample] = ccwPortalVertical;

				for (int sample = 0; sample < sampleCount - 1; sample++)
				{
					AddSourceSegment(corridorCw[armIndex][sample], corridorCw[armIndex][sample + 1], cwHeight[sample], cwHeight[sample + 1], cwVertical[sample], cwVertical[sample + 1], sourceA, sourceB, sourceHeightA, sourceHeightB, sourceVerticalA, sourceVerticalB);
					AddSourceSegment(corridorCcw[armIndex][sample], corridorCcw[armIndex][sample + 1], ccwHeight[sample], ccwHeight[sample + 1], ccwVertical[sample], ccwVertical[sample + 1], sourceA, sourceB, sourceHeightA, sourceHeightB, sourceVerticalA, sourceVerticalB);
					AddSourceSegment(corridorCw[armIndex][sample], corridorCcw[armIndex][sample], cwHeight[sample], ccwHeight[sample], cwVertical[sample], ccwVertical[sample], sourceA, sourceB, sourceHeightA, sourceHeightB, sourceVerticalA, sourceVerticalB);
					AddSourceSegment(corridorCw[armIndex][sample + 1], corridorCcw[armIndex][sample + 1], cwHeight[sample + 1], ccwHeight[sample + 1], cwVertical[sample + 1], ccwVertical[sample + 1], sourceA, sourceB, sourceHeightA, sourceHeightB, sourceVerticalA, sourceVerticalB);
				}

				reportProgress?.Invoke();
			}

			float h = math.max(0.05f, math.min(step, minPortalWidth * 0.5f));
			if (!SweepJunctionPlanDomainBuilder.TryBuild(corridorCw, corridorCcw, portalCw, portalCcw, activePortal, portalOwnership, step, h, out SweepJunctionPlanDomain domain, out string domainFailure))
				return new SweepMeshData { FailureCode = "RibbonDomainInvalid-" + domainFailure };

			if (!SweepJunctionTriangulator.Triangulate(domain, h, ct, reportProgress, out List<float2> vertices2D, out List<int> triangles, out string triangulationFailure))
				return new SweepMeshData { FailureCode = "RibbonTriangulationFailed-" + triangulationFailure };

			float attributeTolerance = math.max(1e-5f, step * 1e-4f);
			var vertices = new Vector3[vertices2D.Count];
			var uvs = new Vector2[vertices2D.Count];
			for (int vertexIndex = 0; vertexIndex < vertices2D.Count; vertexIndex++)
			{
				ct.ThrowIfCancellationRequested();
				float2 plan = vertices2D[vertexIndex];
				bool exactPortal = false;
				float3 position = default;
				for (int armIndex = 0; armIndex < armCount; armIndex++)
				{
					if (activePortal[armIndex] && math.distancesq(plan, portalCw[armIndex]) < 1e-10f)
					{
						exactPortal = true;
						position = portalCwWorld[armIndex];
						break;
					}
					if (activePortal[armIndex] && math.distancesq(plan, portalCcw[armIndex]) < 1e-10f)
					{
						exactPortal = true;
						position = portalCcwWorld[armIndex];
						break;
					}
				}
				if (!exactPortal)
				{
					SampleSource(plan, attributeTolerance, sourceA, sourceB, sourceHeightA, sourceHeightB, sourceVerticalA, sourceVerticalB, out float height, out float vertical);
					position = Position(snapshot, center, axis, e1, e2, plan, height, vertical, ref terrainOutOfBounds);
				}

				vertices[vertexIndex] = new Vector3(position.x, position.y, position.z);
				uvs[vertexIndex] = new Vector2(plan.x * snapshot.UvScale, plan.y * snapshot.UvScale);
				if ((vertexIndex & 1023) == 0)
					reportProgress?.Invoke();
			}

			return new SweepMeshData
			{
				Vertices = vertices,
				Uvs = uvs,
				Triangles = triangles.ToArray(),
				TerrainOutOfBounds = terrainOutOfBounds
			};
		}

		private static Vector3[] CapturedRing(SweepNetworkSnapshot snapshot, SweepNetworkArm arm)
		{
			Vector3[][] rings = arm.AtPieceStart ? snapshot.PieceStartRings : snapshot.PieceEndRings;
			if (rings == null || arm.PieceIndex < 0 || arm.PieceIndex >= rings.Length)
				return null;
			return rings[arm.PieceIndex];
		}

		private static bool TryBuildStripOwnership(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, float3 center, float3 e1, float3 e2, out List<Polygon2D> ownership)
		{
			ownership = null;
			if (snapshot.PieceMeshes == null || arm.PieceIndex < 0 || arm.PieceIndex >= snapshot.PieceMeshes.Length)
				return false;

			SweepMeshData mesh = snapshot.PieceMeshes[arm.PieceIndex];
			if (mesh.Vertices == null || mesh.Triangles == null || mesh.Triangles.Length < 3)
				return false;

			float minimumArea = (float)(1.0 / (PolygonClipper.Scale * PolygonClipper.Scale));
			var triangles = new List<Polygon2D>(mesh.Triangles.Length / 3);
			for (int triangle = 0; triangle < mesh.Triangles.Length; triangle += 3)
			{
				int ia = mesh.Triangles[triangle];
				int ib = mesh.Triangles[triangle + 1];
				int ic = mesh.Triangles[triangle + 2];
				if (ia < 0 || ib < 0 || ic < 0 || ia >= mesh.Vertices.Length || ib >= mesh.Vertices.Length || ic >= mesh.Vertices.Length)
					return false;

				float2 a = Planar(ToFloat3(mesh.Vertices[ia]) - center, e1, e2);
				float2 b = Planar(ToFloat3(mesh.Vertices[ib]) - center, e1, e2);
				float2 c = Planar(ToFloat3(mesh.Vertices[ic]) - center, e1, e2);
				if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)) || !math.all(math.isfinite(c)))
					return false;

				float twiceArea = Cross(b - a, c - a);
				if (math.abs(twiceArea) <= minimumArea * 2f)
					continue;
				if (twiceArea < 0f)
				{
					float2 swap = b;
					b = c;
					c = swap;
				}
				triangles.Add(new Polygon2D { Outer = new[] { a, b, c } });
			}

			if (triangles.Count == 0)
				return false;
			ownership = triangles;
			return true;
		}

		private static float ResolvePortalDirection(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, Vector3[] capturedRing, int firstProfile, int secondProfile, int lastSample)
		{
			if (capturedRing == null)
				return math.dot(arm.ApproachRights[lastSample], arm.Right) < 0f ? -1f : 1f;

			float direct = PortalError(snapshot, arm, capturedRing, firstProfile, secondProfile, lastSample, 1f);
			float flipped = PortalError(snapshot, arm, capturedRing, firstProfile, secondProfile, lastSample, -1f);
			return flipped + 1e-8f < direct ? -1f : 1f;
		}

		private static float[] TransportBasisDirections(SweepNetworkArm arm, float portalDirection)
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

		private static float PortalError(SweepNetworkSnapshot snapshot, SweepNetworkArm arm, Vector3[] capturedRing, int firstProfile, int secondProfile, int lastSample, float direction)
		{
			SweepSnapshot pieces = snapshot.Pieces;
			bool hasTerrain = pieces.Terrain != null;
			BuildSample(pieces, arm.ApproachFrames[lastSample], arm.ApproachRights[lastSample], arm.ApproachUps[lastSample], pieces.ProfilePoints[firstProfile], direction, hasTerrain, out float3 first, out float firstVertical);
			BuildSample(pieces, arm.ApproachFrames[lastSample], arm.ApproachRights[lastSample], arm.ApproachUps[lastSample], pieces.ProfilePoints[secondProfile], direction, hasTerrain, out float3 second, out float secondVertical);
			bool ignored = false;
			first = Drape(snapshot, first, firstVertical, ref ignored);
			second = Drape(snapshot, second, secondVertical, ref ignored);
			return math.distancesq(first, ToFloat3(capturedRing[firstProfile])) + math.distancesq(second, ToFloat3(capturedRing[secondProfile]));
		}

		private static void BuildSample(SweepSnapshot pieces, SweepFrame frame, float3 right, float3 up, float2 profilePoint, float direction, bool hasTerrain, out float3 position, out float vertical)
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
			SweepJunctionMeshBuilder.MakeVertex(hasTerrain, frame.Position, right * direction, up * direction, rotatedLateral, rotatedVertical, out position, out vertical);
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

		private static float3 Position(SweepNetworkSnapshot snapshot, float3 center, float3 axis, float3 e1, float3 e2, float2 plan, float height, float vertical, ref bool terrainOutOfBounds)
		{
			float3 position = center + e1 * plan.x + e2 * plan.y + axis * height;
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

		private static void AddSourceSegment(float2 a, float2 b, float heightA, float heightB, float verticalA, float verticalB, List<float2> sourceA, List<float2> sourceB, List<float> sourceHeightA, List<float> sourceHeightB, List<float> sourceVerticalA, List<float> sourceVerticalB)
		{
			if (math.distancesq(a, b) < 1e-12f)
				return;
			sourceA.Add(a);
			sourceB.Add(b);
			sourceHeightA.Add(heightA);
			sourceHeightB.Add(heightB);
			sourceVerticalA.Add(verticalA);
			sourceVerticalB.Add(verticalB);
		}

		private static bool NormalizePortalEdges(List<float2> boundary, float2[] portalCw, float2[] portalCcw, float tolerance, out int invalidPortal)
		{
			invalidPortal = -1;
			for (int portal = 0; portal < portalCw.Length; portal++)
			{
				int cw = FindPoint(boundary, portalCw[portal]);
				int ccw = FindPoint(boundary, portalCcw[portal]);
				if (cw < 0 || ccw < 0 || cw == ccw)
				{
					invalidPortal = portal;
					return false;
				}
				if ((cw + 1) % boundary.Count == ccw || (ccw + 1) % boundary.Count == cw)
					continue;

				List<int> forward = CollapsiblePath(boundary, cw, ccw, 1, portalCw[portal], portalCcw[portal], portalCw, portalCcw, portal, tolerance);
				List<int> backward = CollapsiblePath(boundary, cw, ccw, -1, portalCw[portal], portalCcw[portal], portalCw, portalCcw, portal, tolerance);
				List<int> remove = forward != null && (backward == null || forward.Count <= backward.Count) ? forward : backward;
				if (remove == null)
				{
					invalidPortal = portal;
					return false;
				}

				remove.Sort();
				for (int index = remove.Count - 1; index >= 0; index--)
					boundary.RemoveAt(remove[index]);
			}
			return true;
		}

		private static List<float2> ResampleBoundary(List<float2> boundary, float2[] portalCw, float2[] portalCcw, float spacing)
		{
			var result = new List<float2>(boundary.Count * 2);
			for (int index = 0; index < boundary.Count; index++)
			{
				float2 a = boundary[index];
				float2 b = boundary[(index + 1) % boundary.Count];
				result.Add(a);
				if (IsPortalEdge(a, b, portalCw, portalCcw))
					continue;

				float length = math.distance(a, b);
				int segments = math.max(1, (int)math.ceil(length / spacing));
				for (int segment = 1; segment < segments; segment++)
					result.Add(math.lerp(a, b, segment / (float)segments));
			}
			return result;
		}

		private static bool IsPortalEdge(float2 a, float2 b, float2[] portalCw, float2[] portalCcw)
		{
			for (int portal = 0; portal < portalCw.Length; portal++)
			{
				bool forward = math.distancesq(a, portalCw[portal]) < 1e-10f && math.distancesq(b, portalCcw[portal]) < 1e-10f;
				bool backward = math.distancesq(a, portalCcw[portal]) < 1e-10f && math.distancesq(b, portalCw[portal]) < 1e-10f;
				if (forward || backward)
					return true;
			}
			return false;
		}

		private static List<int> CollapsiblePath(List<float2> boundary, int start, int end, int direction, float2 portalA, float2 portalB, float2[] portalCw, float2[] portalCcw, int ownPortal, float tolerance)
		{
			var remove = new List<int>();
			int current = start;
			for (int guard = 0; guard < boundary.Count; guard++)
			{
				current = (current + direction + boundary.Count) % boundary.Count;
				if (current == end)
					return remove;

				float2 point = boundary[current];
				Project(point, portalA, portalB, out _, out float distanceSq);
				if (distanceSq > tolerance * tolerance || IsOtherPortal(point, portalCw, portalCcw, ownPortal))
					return null;
				remove.Add(current);
			}
			return null;
		}

		private static bool IsOtherPortal(float2 point, float2[] portalCw, float2[] portalCcw, int ownPortal)
		{
			for (int portal = 0; portal < portalCw.Length; portal++)
			{
				if (portal == ownPortal)
					continue;
				if (math.distancesq(point, portalCw[portal]) < 1e-10f || math.distancesq(point, portalCcw[portal]) < 1e-10f)
					return true;
			}
			return false;
		}

		private static int FindPoint(List<float2> points, float2 point)
		{
			for (int index = 0; index < points.Count; index++)
			{
				if (math.distancesq(points[index], point) < 1e-10f)
					return index;
			}
			return -1;
		}

		private static void SampleSource(float2 point, float tolerance, List<float2> sourceA, List<float2> sourceB, List<float> sourceHeightA, List<float> sourceHeightB, List<float> sourceVerticalA, List<float> sourceVerticalB, out float height, out float vertical)
		{
			float bestDistanceSq = float.MaxValue;
			for (int segment = 0; segment < sourceA.Count; segment++)
			{
				Project(point, sourceA[segment], sourceB[segment], out _, out float distanceSq);
				bestDistanceSq = math.min(bestDistanceSq, distanceSq);
			}

			float thresholdSq = bestDistanceSq + tolerance * tolerance;
			float sumWeight = 0f;
			float sumHeight = 0f;
			float sumVertical = 0f;
			for (int segment = 0; segment < sourceA.Count; segment++)
			{
				Project(point, sourceA[segment], sourceB[segment], out float t, out float distanceSq);
				if (distanceSq > thresholdSq)
					continue;
				float weight = 1f / math.max(1e-12f, distanceSq + tolerance * tolerance * 0.01f);
				sumWeight += weight;
				sumHeight += weight * math.lerp(sourceHeightA[segment], sourceHeightB[segment], t);
				sumVertical += weight * math.lerp(sourceVerticalA[segment], sourceVerticalB[segment], t);
			}

			if (sumWeight <= 0f)
			{
				height = 0f;
				vertical = 0f;
				return;
			}

			height = sumHeight / sumWeight;
			vertical = sumVertical / sumWeight;
		}

		private static void Project(float2 point, float2 a, float2 b, out float t, out float distanceSq)
		{
			float2 ab = b - a;
			float lengthSq = math.dot(ab, ab);
			t = lengthSq > 1e-12f ? math.saturate(math.dot(point - a, ab) / lengthSq) : 0f;
			distanceSq = math.distancesq(point, a + ab * t);
		}

		private static float2 Planar(float3 relative, float3 e1, float3 e2)
		{
			return new float2(math.dot(relative, e1), math.dot(relative, e2));
		}

		private static float Cross(float2 a, float2 b)
		{
			return a.x * b.y - a.y * b.x;
		}

		private static float3 ToFloat3(Vector3 value)
		{
			return new float3(value.x, value.y, value.z);
		}
	}
}
