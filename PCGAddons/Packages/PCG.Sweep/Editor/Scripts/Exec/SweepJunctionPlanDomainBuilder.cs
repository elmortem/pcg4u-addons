using System;
using System.Collections.Generic;
using PCG.Polygons;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal static class SweepJunctionPlanDomainBuilder
	{
		internal static bool TryBuild(
			float2[][] corridorCw,
			float2[][] corridorCcw,
			float2[] portalCw,
			float2[] portalCcw,
			bool[] activePortal,
			List<Polygon2D>[] portalOwnership,
			float step,
			float spacing,
			out SweepJunctionPlanDomain result,
			out string failure)
		{
			result = null;
			failure = null;

			if (!ValidateInputs(corridorCw, corridorCcw, portalCw, portalCcw, activePortal, portalOwnership, step, spacing, out failure))
				return false;

			int armCount = corridorCw.Length;
			int activeCount = 0;
			var gates = new List<Polygon2D>[armCount];
			for (int arm = 0; arm < armCount; arm++)
			{
				if (activePortal[arm])
					activeCount++;
			}

			float quantization = (float)(1.0 / PolygonClipper.Scale);
			float tolerance = math.max(quantization * math.max(3f, (activeCount + 2) * 1.5f), math.max(step, spacing) * 1e-6f);
			float areaTolerance = quantization * quantization;
			var cells = new List<Polygon2D>();
			for (int arm = 0; arm < armCount; arm++)
			{
				float2[] cw = corridorCw[arm];
				float2[] ccw = corridorCcw[arm];
				for (int sample = 0; sample < cw.Length - 1; sample++)
				{
					var ring = new[]
					{
						Quantize(cw[sample]),
						Quantize(ccw[sample]),
						Quantize(ccw[sample + 1]),
						Quantize(cw[sample + 1])
					};
					double area = SignedArea(ring);
					if (math.abs((float)area) <= areaTolerance)
						continue;
					if (area < 0.0)
						Array.Reverse(ring);
					cells.Add(new Polygon2D { Outer = ring });
				}
			}

			if (cells.Count == 0)
			{
				failure = "DomainCellsEmpty";
				return false;
			}

			List<Polygon2D> domain;
			try
			{
				domain = PolygonClipper.Union(cells, Array.Empty<Polygon2D>());
			}
			catch
			{
				failure = "DomainUnionFailed";
				return false;
			}

			if (!ValidateSingleComponent(domain, "DomainUnion", out failure))
				return false;

			for (int pass = 0; pass < 2; pass++)
			{
				bool exactPass = pass == 1;
				for (int arm = 0; arm < armCount; arm++)
				{
					if (!activePortal[arm])
						continue;
					bool exactOwnership = portalOwnership[arm] != null;
					if (exactOwnership != exactPass)
						continue;

					List<Polygon2D> gate;
					string gateFailure;
					if (exactOwnership)
					{
						if (!TryBuildOwnershipCut(domain, portalOwnership[arm], areaTolerance, out gate, out gateFailure))
						{
							failure = gateFailure + "-" + arm;
							return false;
						}
					}
					else if (!TryBuildContinuationGate(domain, corridorCw[arm], corridorCcw[arm], portalCw[arm], portalCcw[arm], tolerance, areaTolerance, out gate, out gateFailure))
					{
						failure = gateFailure + "-" + arm;
						return false;
					}
					gates[arm] = gate;

					if (gate.Count > 0)
					{
						try
						{
							domain = PolygonClipper.Difference(domain, gate);
						}
						catch
						{
							failure = "PortalCutFailed-" + arm;
							return false;
						}
					}

					if (exactOwnership)
					{
						if (!SelectMaterialComponents(domain, arm, areaTolerance, out domain, out failure))
							return false;
					}
					else if (!SelectCenterComponent(domain, arm, tolerance, areaTolerance, out domain, out failure))
					{
						return false;
					}
				}
			}

			int componentCount = domain.Count;
			var componentLoopStarts = new int[componentCount];
			int loopCount = 0;
			for (int component = 0; component < componentCount; component++)
			{
				componentLoopStarts[component] = loopCount;
				loopCount += 1 + (domain[component].Holes?.Count ?? 0);
			}
			var loops = new List<float2>[loopCount];
			var locked = new List<bool>[loops.Length];
			for (int component = 0; component < componentCount; component++)
			{
				Polygon2D polygon = domain[component];
				int holeCount = polygon.Holes?.Count ?? 0;
				for (int localLoop = 0; localLoop <= holeCount; localLoop++)
				{
					int loop = componentLoopStarts[component] + localLoop;
					float2[] source = localLoop == 0 ? polygon.Outer : polygon.Holes[localLoop - 1];
					if (source == null || source.Length < 3 || math.abs((float)SignedArea(source)) <= areaTolerance)
					{
						failure = localLoop == 0 ? "DomainBoundaryDegenerate-" + component : "DomainHoleDegenerate-" + component + "-" + (localLoop - 1);
						return false;
					}
					loops[loop] = new List<float2>(source);
					bool counterClockwise = SignedArea(loops[loop]) > 0.0;
					if (counterClockwise != (localLoop == 0))
						loops[loop].Reverse();
					locked[loop] = new List<bool>(loops[loop].Count);
					for (int point = 0; point < loops[loop].Count; point++)
						locked[loop].Add(false);
				}
			}

			var portalLoops = new int[armCount];
			for (int arm = 0; arm < portalLoops.Length; arm++)
				portalLoops[arm] = -1;

			for (int arm = 0; arm < armCount; arm++)
			{
				if (!activePortal[arm])
					continue;

				int portalLoop = FindPortalLoop(loops, portalCw[arm], portalCcw[arm], tolerance, out float cwDistance, out float ccwDistance);
				if (portalLoop < 0)
				{
					if (cwDistance > tolerance)
						failure = "PortalCwOutside-" + arm + "-" + cwDistance.ToString("F6") + "-cut" + FindContainingGate(gates, portalCw[arm], arm);
					else if (ccwDistance > tolerance)
						failure = "PortalCcwOutside-" + arm + "-" + ccwDistance.ToString("F6") + "-cut" + FindContainingGate(gates, portalCcw[arm], arm);
					else
						failure = "PortalLoopMismatch-" + arm;
					return false;
				}
				portalLoops[arm] = portalLoop;
				if (!LockBoundaryPoint(loops[portalLoop], locked[portalLoop], portalCw[arm], tolerance, out cwDistance))
				{
					failure = "PortalCwOutside-" + arm + "-" + cwDistance.ToString("F6") + "-cut" + FindContainingGate(gates, portalCw[arm], arm);
					return false;
				}
				if (!LockBoundaryPoint(loops[portalLoop], locked[portalLoop], portalCcw[arm], tolerance, out ccwDistance))
				{
					failure = "PortalCcwOutside-" + arm + "-" + ccwDistance.ToString("F6") + "-cut" + FindContainingGate(gates, portalCcw[arm], arm);
					return false;
				}
			}

			for (int arm = 0; arm < armCount; arm++)
			{
				if (!activePortal[arm])
					continue;
				int loop = portalLoops[arm];
				string collapseFailure = "LoopMissing";
				if (loop < 0 || !CollapsePortalSegment(loops[loop], locked[loop], portalCw[arm], portalCcw[arm], tolerance, out collapseFailure))
				{
					failure = "PortalEdgeInvalid-" + arm + "-" + (portalOwnership[arm] == null ? "Patch" : "Strip") + "-A" + Coordinate(portalCw[arm]) + "-B" + Coordinate(portalCcw[arm]) + "-" + collapseFailure;
					return false;
				}
			}

			for (int component = 0; component < componentCount; component++)
			{
				int holeCount = domain[component].Holes?.Count ?? 0;
				for (int localLoop = 0; localLoop <= holeCount; localLoop++)
				{
					int loop = componentLoopStarts[component] + localLoop;
					if (loops[loop].Count < 3 || math.abs((float)SignedArea(loops[loop])) <= areaTolerance)
					{
						failure = localLoop == 0 ? "DomainBoundaryDegenerate-" + component : "DomainHoleDegenerate-" + component + "-" + (localLoop - 1);
						return false;
					}
					bool counterClockwise = SignedArea(loops[loop]) > 0.0;
					if (counterClockwise != (localLoop == 0))
						loops[loop].Reverse();
				}
			}

			var sourceTags = new int[loops.Length][];
			for (int loop = 0; loop < loops.Length; loop++)
			{
				sourceTags[loop] = new int[loops[loop].Count];
				for (int edge = 0; edge < sourceTags[loop].Length; edge++)
					sourceTags[loop][edge] = -1;
			}
			var portalEdgeCounts = new int[armCount];
			for (int loop = 0; loop < loops.Length; loop++)
			{
				for (int edge = 0; edge < loops[loop].Count; edge++)
				{
					float2 a = loops[loop][edge];
					float2 b = loops[loop][(edge + 1) % loops[loop].Count];
					for (int arm = 0; arm < armCount; arm++)
					{
						if (!activePortal[arm] || !MatchesEdge(a, b, portalCw[arm], portalCcw[arm]))
							continue;
						if (sourceTags[loop][edge] >= 0)
						{
							failure = "PortalEdgeConflict-" + sourceTags[loop][edge] + "-" + arm;
							return false;
						}
						sourceTags[loop][edge] = arm;
						portalEdgeCounts[arm]++;
					}
				}
			}

			for (int arm = 0; arm < armCount; arm++)
			{
				if (activePortal[arm] && portalEdgeCounts[arm] != 1)
				{
					failure = "PortalEdgeCount-" + arm + "-" + portalEdgeCounts[arm];
					return false;
				}
			}

			var boundaries = new float2[loops.Length][];
			var edgePortalArms = new int[loops.Length][];
			for (int loop = 0; loop < loops.Length; loop++)
				Resample(loops[loop], sourceTags[loop], spacing, out boundaries[loop], out edgePortalArms[loop]);
			var components = new SweepJunctionPlanComponent[componentCount];
			for (int component = 0; component < componentCount; component++)
			{
				int start = componentLoopStarts[component];
				int holeCount = domain[component].Holes?.Count ?? 0;
				var holes = new float2[holeCount][];
				var holeEdgePortalArms = new int[holeCount][];
				for (int hole = 0; hole < holeCount; hole++)
				{
					holes[hole] = boundaries[start + hole + 1];
					holeEdgePortalArms[hole] = edgePortalArms[start + hole + 1];
				}
				components[component] = new SweepJunctionPlanComponent
				{
					Outer = boundaries[start],
					Holes = holes,
					OuterEdgePortalArms = edgePortalArms[start],
					HoleEdgePortalArms = holeEdgePortalArms
				};
			}
			result = new SweepJunctionPlanDomain
			{
				Components = components
			};
			return true;
		}

		private static int FindPortalLoop(List<float2>[] loops, float2 a, float2 b, float tolerance, out float distanceA, out float distanceB)
		{
			distanceA = float.MaxValue;
			distanceB = float.MaxValue;
			int best = -1;
			float bestScore = float.MaxValue;
			for (int loop = 0; loop < loops.Length; loop++)
			{
				float candidateA = DistanceToLoop(loops[loop], a);
				float candidateB = DistanceToLoop(loops[loop], b);
				distanceA = math.min(distanceA, candidateA);
				distanceB = math.min(distanceB, candidateB);
				float score = math.max(candidateA, candidateB);
				if (score <= tolerance && score < bestScore)
				{
					best = loop;
					bestScore = score;
				}
			}
			return best;
		}

		private static float DistanceToLoop(List<float2> loop, float2 point)
		{
			float bestDistanceSq = float.MaxValue;
			for (int edge = 0; edge < loop.Count; edge++)
			{
				Project(point, loop[edge], loop[(edge + 1) % loop.Count], out _, out float distanceSq);
				bestDistanceSq = math.min(bestDistanceSq, distanceSq);
			}
			return math.sqrt(bestDistanceSq);
		}

		private static bool ValidateInputs(float2[][] corridorCw, float2[][] corridorCcw, float2[] portalCw, float2[] portalCcw, bool[] activePortal, List<Polygon2D>[] portalOwnership, float step, float spacing, out string failure)
		{
			failure = null;
			if (corridorCw == null || corridorCcw == null || portalCw == null || portalCcw == null || activePortal == null || portalOwnership == null)
			{
				failure = "DomainInputMissing";
				return false;
			}
			int count = corridorCw.Length;
			if (count < 1 || corridorCcw.Length != count || portalCw.Length != count || portalCcw.Length != count || activePortal.Length != count || portalOwnership.Length != count)
			{
				failure = "DomainInputSizeMismatch";
				return false;
			}
			if (!math.isfinite(step) || !math.isfinite(spacing) || step <= 0f || spacing <= 0f)
			{
				failure = "DomainSpacingInvalid";
				return false;
			}

			for (int arm = 0; arm < count; arm++)
			{
				float2[] cw = corridorCw[arm];
				float2[] ccw = corridorCcw[arm];
				if (cw == null || ccw == null || cw.Length < 2 || cw.Length != ccw.Length)
				{
					failure = "DomainCorridorInvalid-" + arm;
					return false;
				}
				for (int sample = 0; sample < cw.Length; sample++)
				{
					if (!IsCoordinateValid(cw[sample]) || !IsCoordinateValid(ccw[sample]))
					{
						failure = "DomainCoordinateInvalid-" + arm + "-" + sample;
						return false;
					}
				}
				if (activePortal[arm] && (!IsCoordinateValid(portalCw[arm]) || !IsCoordinateValid(portalCcw[arm])))
				{
					failure = "PortalCoordinateInvalid-" + arm;
					return false;
				}
			}
			return true;
		}

		private static bool IsCoordinateValid(float2 point)
		{
			const float limit = 1000000000000f;
			return math.all(math.isfinite(point)) && math.all(math.abs(point) < limit);
		}

		private static bool ValidateSingleComponent(List<Polygon2D> polygons, string stage, out string failure)
		{
			failure = null;
			if (polygons == null || polygons.Count == 0)
			{
				failure = stage + "Empty";
				return false;
			}
			if (polygons.Count != 1)
			{
				failure = stage + "Components-" + polygons.Count;
				return false;
			}
			Polygon2D polygon = polygons[0];
			if (polygon == null || polygon.Outer == null || polygon.Outer.Length < 3)
			{
				failure = stage + "BoundaryInvalid";
				return false;
			}
			return true;
		}

		private static bool TryBuildOwnershipCut(List<Polygon2D> source, List<Polygon2D> ownership, float areaTolerance, out List<Polygon2D> cut, out string failure)
		{
			cut = new List<Polygon2D>();
			failure = null;
			if (ownership.Count == 0)
			{
				failure = "PortalOwnershipEmpty";
				return false;
			}
			List<Polygon2D> intersection;
			try
			{
				intersection = PolygonClipper.Intersection(source, ownership);
			}
			catch
			{
				failure = "PortalOwnershipClipFailed";
				return false;
			}

			for (int polygon = 0; polygon < intersection.Count; polygon++)
			{
				Polygon2D component = intersection[polygon];
				if (component == null || component.Outer == null || component.Outer.Length < 3)
					continue;

				double area = math.abs((float)SignedArea(component.Outer));
				for (int hole = 0; hole < component.Holes.Count; hole++)
					area -= math.abs((float)SignedArea(component.Holes[hole]));
				if (area > areaTolerance)
					cut.Add(component);
			}
			return true;
		}

		private static bool TryBuildContinuationGate(List<Polygon2D> source, float2[] corridorCw, float2[] corridorCcw, float2 rawA, float2 rawB, float tolerance, float areaTolerance, out List<Polygon2D> gate, out string failure)
		{
			gate = new List<Polygon2D>();
			failure = null;
			float2 a = Quantize(rawA);
			float2 b = Quantize(rawB);
			float2 chord = b - a;
			float chordLength = math.length(chord);
			if (chordLength <= tolerance)
			{
				failure = "PortalChordDegenerate";
				return false;
			}

			int last = corridorCw.Length - 1;
			float2 previousMiddle = (corridorCw[last - 1] + corridorCcw[last - 1]) * 0.5f;
			float2 portalMiddle = (corridorCw[last] + corridorCcw[last]) * 0.5f;
			float2 outward = portalMiddle - previousMiddle;
			float outwardLength = math.length(outward);
			if (outwardLength <= tolerance)
			{
				failure = "PortalContinuationDegenerate";
				return false;
			}
			outward /= outwardLength;

			float2 middle = (a + b) * 0.5f;
			float maximumForward = float.MinValue;
			float minimumForward = math.min(math.dot(a - middle, outward), math.dot(b - middle, outward));
			float2 minimum = source[0].Outer[0];
			float2 maximum = source[0].Outer[0];
			for (int polygon = 0; polygon < source.Count; polygon++)
			{
				float2[] ring = source[polygon].Outer;
				for (int i = 0; i < ring.Length; i++)
				{
					float2 relative = ring[i] - middle;
					maximumForward = math.max(maximumForward, math.dot(relative, outward));
					minimum = math.min(minimum, ring[i]);
					maximum = math.max(maximum, ring[i]);
				}
			}
			float diameter = math.length(maximum - minimum);
			float margin = math.max(1f, diameter * 0.25f + tolerance * 4f);
			float distance = math.max(margin, maximumForward - minimumForward + margin);
			float2 shift = outward * distance;
			if (math.abs(chord.x * shift.y - chord.y * shift.x) <= tolerance * tolerance)
			{
				failure = "PortalContinuationParallel";
				return false;
			}
			var sweep = new Polygon2D
			{
				Outer = new[] { a, a + shift, b + shift, b }
			};
			if (SignedArea(sweep.Outer) < 0.0)
				Array.Reverse(sweep.Outer);

			List<Polygon2D> fragments;
			try
			{
				fragments = PolygonClipper.Intersection(source, new[] { sweep });
			}
			catch
			{
				failure = "PortalContinuationClipFailed";
				return false;
			}

			float probeDistance = (float)(4.0 / PolygonClipper.Scale);
			float2 probeShift = outward * probeDistance;
			var seed = new Polygon2D
			{
				Outer = new[] { a, a + probeShift, b + probeShift, b }
			};
			if (SignedArea(seed.Outer) < 0.0)
				Array.Reverse(seed.Outer);

			try
			{
				float contactTolerance = (float)(1.5 / PolygonClipper.Scale);
				for (int i = 0; i < fragments.Count; i++)
				{
					Polygon2D fragment = fragments[i];
					if (fragment == null || fragment.Outer == null || fragment.Outer.Length < 3)
						continue;
					if (!TouchesPortalBase(fragment.Outer, a, b, chordLength, contactTolerance))
						continue;
					if (!IntersectsArea(fragment, seed, areaTolerance))
						continue;
					gate.Add(fragment);
				}
			}
			catch
			{
				failure = "PortalContinuationSeedFailed";
				return false;
			}
			return true;
		}

		private static bool TouchesPortalBase(float2[] ring, float2 a, float2 b, float chordLength, float tolerance)
		{
			float2 chord = b - a;
			float chordLengthSq = chordLength * chordLength;
			float toleranceSq = tolerance * tolerance;
			for (int edge = 0; edge < ring.Length; edge++)
			{
				float2 p = ring[edge];
				float2 q = ring[(edge + 1) % ring.Length];
				float tp = math.dot(p - a, chord) / chordLengthSq;
				float tq = math.dot(q - a, chord) / chordLengthSq;
				float2 projectedP = a + chord * tp;
				float2 projectedQ = a + chord * tq;
				if (math.distancesq(p, projectedP) > toleranceSq || math.distancesq(q, projectedQ) > toleranceSq)
					continue;
				float overlapStart = math.max(0f, math.min(tp, tq));
				float overlapEnd = math.min(1f, math.max(tp, tq));
				if ((overlapEnd - overlapStart) * chordLength > tolerance)
					return true;
			}
			return false;
		}

		private static bool IntersectsArea(Polygon2D polygon, Polygon2D region, float areaTolerance)
		{
			List<Polygon2D> intersection = PolygonClipper.Intersection(new[] { polygon }, new[] { region });
			double area = 0.0;
			for (int i = 0; i < intersection.Count; i++)
			{
				Polygon2D component = intersection[i];
				if (component == null || component.Outer == null)
					continue;
				area += math.abs((float)SignedArea(component.Outer));
				for (int hole = 0; hole < component.Holes.Count; hole++)
					area -= math.abs((float)SignedArea(component.Holes[hole]));
			}
			return area > areaTolerance;
		}

		private static bool SelectMaterialComponents(List<Polygon2D> polygons, int arm, float areaTolerance, out List<Polygon2D> selected, out string failure)
		{
			selected = new List<Polygon2D>();
			failure = null;
			if (polygons == null || polygons.Count == 0)
			{
				failure = "PortalCutEmpty-" + arm;
				return false;
			}

			for (int i = 0; i < polygons.Count; i++)
			{
				Polygon2D polygon = polygons[i];
				if (polygon == null || polygon.Outer == null || polygon.Outer.Length < 3)
					continue;
				if (MaterialArea(polygon) <= areaTolerance)
					continue;
				selected.Add(polygon);
			}
			if (selected.Count == 0)
			{
				failure = "PortalCutMaterialEmpty-" + arm;
				return false;
			}
			return true;
		}

		private static bool SelectCenterComponent(List<Polygon2D> polygons, int arm, float tolerance, float areaTolerance, out List<Polygon2D> selected, out string failure)
		{
			selected = null;
			failure = null;
			if (polygons == null || polygons.Count == 0)
			{
				failure = "PortalCutEmpty-" + arm;
				return false;
			}

			Polygon2D centerComponent = null;
			for (int i = 0; i < polygons.Count; i++)
			{
				Polygon2D polygon = polygons[i];
				if (polygon == null || polygon.Outer == null || polygon.Outer.Length < 3)
					continue;
				if (MaterialArea(polygon) <= areaTolerance)
					continue;
				if (ContainsOrTouchesOrigin(polygon, tolerance))
				{
					if (centerComponent != null)
					{
						failure = "PortalCutCenterComponents-" + arm;
						return false;
					}
					centerComponent = polygon;
				}
			}

			if (centerComponent == null)
			{
				failure = "PortalCutCenterMissing-" + arm;
				return false;
			}

			selected = new List<Polygon2D> { centerComponent };
			return true;
		}

		private static double MaterialArea(Polygon2D polygon)
		{
			double area = math.abs((float)SignedArea(polygon.Outer));
			for (int hole = 0; hole < polygon.Holes.Count; hole++)
				area -= math.abs((float)SignedArea(polygon.Holes[hole]));
			return area;
		}

		private static bool ContainsOrTouchesOrigin(Polygon2D polygon, float tolerance)
		{
			float2 origin = float2.zero;
			if (polygon.Contains(origin))
				return true;
			float toleranceSq = tolerance * tolerance;
			for (int i = 0; i < polygon.Outer.Length; i++)
			{
				Project(origin, polygon.Outer[i], polygon.Outer[(i + 1) % polygon.Outer.Length], out _, out float distanceSq);
				if (distanceSq <= toleranceSq)
					return true;
			}
			for (int hole = 0; hole < polygon.Holes.Count; hole++)
			{
				float2[] ring = polygon.Holes[hole];
				for (int i = 0; i < ring.Length; i++)
				{
					Project(origin, ring[i], ring[(i + 1) % ring.Length], out _, out float distanceSq);
					if (distanceSq <= toleranceSq)
						return true;
				}
			}
			return false;
		}

		private static int FindContainingGate(List<Polygon2D>[] gates, float2 point, int ownArm)
		{
			for (int arm = 0; arm < gates.Length; arm++)
			{
				if (arm == ownArm || gates[arm] == null)
					continue;
				for (int i = 0; i < gates[arm].Count; i++)
				{
					if (gates[arm][i].Contains(point))
						return arm;
				}
			}
			return -1;
		}

		private static bool LockBoundaryPoint(List<float2> points, List<bool> locked, float2 point, float tolerance, out float distance)
		{
			const float exactToleranceSq = 1e-14f;
			for (int i = 0; i < points.Count; i++)
			{
				if (math.distancesq(points[i], point) <= exactToleranceSq)
				{
					points[i] = point;
					locked[i] = true;
					distance = 0f;
					return true;
				}
			}

			float toleranceSq = tolerance * tolerance;
			int nearestVertex = -1;
			float nearestVertexDistanceSq = float.MaxValue;
			for (int i = 0; i < points.Count; i++)
			{
				if (locked[i])
					continue;
				float distanceSq = math.distancesq(points[i], point);
				if (distanceSq < nearestVertexDistanceSq)
				{
					nearestVertexDistanceSq = distanceSq;
					nearestVertex = i;
				}
			}
			if (nearestVertex >= 0 && nearestVertexDistanceSq <= toleranceSq)
			{
				points[nearestVertex] = point;
				locked[nearestVertex] = true;
				distance = math.sqrt(nearestVertexDistanceSq);
				return true;
			}

			int nearestEdge = -1;
			float nearestEdgeDistanceSq = float.MaxValue;
			for (int i = 0; i < points.Count; i++)
			{
				Project(point, points[i], points[(i + 1) % points.Count], out _, out float distanceSq);
				if (distanceSq < nearestEdgeDistanceSq)
				{
					nearestEdgeDistanceSq = distanceSq;
					nearestEdge = i;
				}
			}
			distance = math.sqrt(nearestEdgeDistanceSq);
			if (nearestEdge < 0 || nearestEdgeDistanceSq > toleranceSq)
				return false;

			points.Insert(nearestEdge + 1, point);
			locked.Insert(nearestEdge + 1, true);
			return true;
		}

		private static bool CollapsePortalSegment(List<float2> points, List<bool> locked, float2 a, float2 b, float tolerance, out string failure)
		{
			failure = null;
			int start = FindExact(points, a);
			int end = FindExact(points, b);
			if (start < 0 || end < 0 || start == end)
			{
				failure = "Endpoints-" + start + "-" + end;
				return false;
			}
			if ((start + 1) % points.Count == end || (end + 1) % points.Count == start)
				return true;

			List<int> forward = CollectPortalIntermediates(points, locked, start, end, 1, a, b, tolerance, out string forwardFailure);
			List<int> backward = CollectPortalIntermediates(points, locked, start, end, -1, a, b, tolerance, out string backwardFailure);
			List<int> remove = forward != null && (backward == null || forward.Count <= backward.Count) ? forward : backward;
			if (remove == null)
			{
				failure = "F" + forwardFailure + "-B" + backwardFailure;
				return false;
			}

			remove.Sort();
			for (int i = remove.Count - 1; i >= 0; i--)
			{
				points.RemoveAt(remove[i]);
				locked.RemoveAt(remove[i]);
			}

			start = FindExact(points, a);
			end = FindExact(points, b);
			bool valid = start >= 0 && end >= 0 && ((start + 1) % points.Count == end || (end + 1) % points.Count == start);
			if (!valid)
				failure = "Result-" + start + "-" + end + "-" + points.Count;
			return valid;
		}

		private static List<int> CollectPortalIntermediates(List<float2> points, List<bool> locked, int start, int end, int direction, float2 a, float2 b, float tolerance, out string failure)
		{
			failure = null;
			var result = new List<int>();
			float2 ab = b - a;
			float lengthSq = math.dot(ab, ab);
			float parameterTolerance = tolerance / math.sqrt(lengthSq);
			int current = start;
			for (int guard = 0; guard < points.Count; guard++)
			{
				current = (current + direction + points.Count) % points.Count;
				if (current == end)
					return result;
				if (locked[current])
				{
					failure = "Locked-" + current + "-" + Coordinate(points[current]);
					return null;
				}
				float rawT = math.dot(points[current] - a, ab) / lengthSq;
				Project(points[current], a, b, out _, out float distanceSq);
				if (rawT < -parameterTolerance || rawT > 1f + parameterTolerance || distanceSq > tolerance * tolerance)
				{
					failure = "Off-" + current + "-t" + rawT.ToString("F5") + "-d" + math.sqrt(distanceSq).ToString("F5") + "-" + Coordinate(points[current]);
					return null;
				}
				result.Add(current);
			}
			failure = "Guard";
			return null;
		}

		private static string Coordinate(float2 point)
		{
			return point.x.ToString("F5") + "_" + point.y.ToString("F5");
		}

		private static int FindExact(List<float2> points, float2 point)
		{
			for (int i = 0; i < points.Count; i++)
			{
				if (math.distancesq(points[i], point) <= 1e-14f)
					return i;
			}
			return -1;
		}

		private static bool MatchesEdge(float2 a, float2 b, float2 portalA, float2 portalB)
		{
			const float exactToleranceSq = 1e-14f;
			bool direct = math.distancesq(a, portalA) <= exactToleranceSq && math.distancesq(b, portalB) <= exactToleranceSq;
			bool reverse = math.distancesq(a, portalB) <= exactToleranceSq && math.distancesq(b, portalA) <= exactToleranceSq;
			return direct || reverse;
		}

		private static void Resample(List<float2> source, int[] sourceTags, float spacing, out float2[] boundary, out int[] edgePortalArms)
		{
			var points = new List<float2>(source.Count * 2);
			var tags = new List<int>(source.Count * 2);
			for (int edge = 0; edge < source.Count; edge++)
			{
				float2 a = source[edge];
				float2 b = source[(edge + 1) % source.Count];
				int segmentCount = sourceTags[edge] >= 0 ? 1 : math.max(1, (int)math.ceil(math.distance(a, b) / spacing));
				points.Add(a);
				tags.Add(sourceTags[edge]);
				for (int segment = 1; segment < segmentCount; segment++)
				{
					points.Add(math.lerp(a, b, segment / (float)segmentCount));
					tags.Add(sourceTags[edge]);
				}
			}
			boundary = points.ToArray();
			edgePortalArms = tags.ToArray();
		}

		private static void Project(float2 point, float2 a, float2 b, out float t, out float distanceSq)
		{
			float2 ab = b - a;
			float lengthSq = math.dot(ab, ab);
			t = lengthSq > 1e-12f ? math.saturate(math.dot(point - a, ab) / lengthSq) : 0f;
			distanceSq = math.distancesq(point, a + ab * t);
		}

		private static float2 Quantize(float2 point)
		{
			double scale = PolygonClipper.Scale;
			long x = (long)(point.x * scale);
			long y = (long)(point.y * scale);
			return new float2((float)(x / scale), (float)(y / scale));
		}

		private static double SignedArea(IReadOnlyList<float2> ring)
		{
			double area = 0.0;
			for (int i = 0; i < ring.Count; i++)
			{
				float2 a = ring[i];
				float2 b = ring[(i + 1) % ring.Count];
				area += (double)a.x * b.y - (double)b.x * a.y;
			}
			return area * 0.5;
		}
	}
}
