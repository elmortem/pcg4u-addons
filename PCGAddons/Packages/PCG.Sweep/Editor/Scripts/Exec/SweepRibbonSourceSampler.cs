using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepRibbonSourceSampler
	{
		private const int MaxCellsPerTriangle = 4096;
		private const float BarycentricTolerance = 1e-4f;
		private readonly SweepRibbonSourceTriangle[] _sources;
		private readonly Dictionary<(int, int), List<int>> _cells = new Dictionary<(int, int), List<int>>();
		private readonly List<int> _large = new List<int>();
		private readonly float _cellSize;
		private readonly float _heightTolerance;
		private readonly bool _envelope;
		private readonly bool _minimumEnvelope;

		internal SweepRibbonSourceSampler(SweepRibbonSourceTriangle[] sources, float cellSize, float heightTolerance)
			: this(sources, cellSize, heightTolerance, false, true)
		{
		}

		internal SweepRibbonSourceSampler(SweepRibbonSourceTriangle[] sources, float cellSize, float heightTolerance, bool envelope, bool minimumEnvelope)
		{
			_sources = sources;
			_cellSize = math.max(0.05f, cellSize);
			_heightTolerance = math.max(1e-4f, heightTolerance);
			_envelope = envelope;
			_minimumEnvelope = minimumEnvelope;
			BuildIndex();
		}

		internal bool TrySample(float2 point, out float height, out float2 uv, out string failure)
		{
			height = 0f;
			uv = float2.zero;
			failure = null;
			var candidates = Candidates(point);
			bool found = false;
			bool hasApproach = false;
			bool selectedStrip = false;
			int selectedOrder = int.MaxValue;
			SweepRibbonSourceTriangle selectedSource = null;
			float selectedHeight = 0f;
			float2 selectedUv = float2.zero;
			float minimumHeight = float.MaxValue;
			float maximumHeight = float.MinValue;
			SweepRibbonSourceTriangle minimumSource = null;
			SweepRibbonSourceTriangle maximumSource = null;
			for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
			{
				SweepRibbonSourceTriangle source = _sources[candidates[candidateIndex]];
				if (!TryBarycentric(source, point, BarycentricTolerance, out float3 weights))
					continue;
				float candidateHeight = weights.x * source.WorldA.y + weights.y * source.WorldB.y + weights.z * source.WorldC.y;
				float2 candidateUv = weights.x * source.UvA + weights.y * source.UvB + weights.z * source.UvC;
				hasApproach |= !source.Strip;
				if (candidateHeight < minimumHeight)
				{
					minimumHeight = candidateHeight;
					minimumSource = source;
				}
				if (candidateHeight > maximumHeight)
				{
					maximumHeight = candidateHeight;
					maximumSource = source;
				}
				bool better = !found || _envelope && BetterEnvelope(candidateHeight, selectedHeight, source, selectedSource) || !_envelope && (source.Strip && !selectedStrip || source.Strip == selectedStrip && source.SourceOrder < selectedOrder);
				if (better)
				{
					found = true;
					selectedStrip = source.Strip;
					selectedOrder = source.SourceOrder;
					selectedSource = source;
					selectedHeight = candidateHeight;
					selectedUv = candidateUv;
				}
			}

			if (found)
			{
				if (!_envelope && maximumHeight - minimumHeight > _heightTolerance && !hasApproach)
				{
					failure = HeightConflictFailure(point, maximumHeight - minimumHeight, selectedSource, selectedHeight, minimumSource, minimumHeight, maximumSource, maximumHeight);
					return false;
				}
				height = selectedHeight;
				uv = selectedUv;
				return true;
			}

			float bestDistanceSq = float.MaxValue;
			int bestSource = -1;
			float3 bestWeights = default;
			for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
			{
				ClosestWeights(_sources[sourceIndex], point, out float3 weights, out float distanceSq);
				float candidateHeight = weights.x * _sources[sourceIndex].WorldA.y + weights.y * _sources[sourceIndex].WorldB.y + weights.z * _sources[sourceIndex].WorldC.y;
				float currentHeight = bestSource < 0 ? 0f : bestWeights.x * _sources[bestSource].WorldA.y + bestWeights.y * _sources[bestSource].WorldB.y + bestWeights.z * _sources[bestSource].WorldC.y;
				bool closer = distanceSq < bestDistanceSq - 1e-12f;
				bool tied = math.abs(distanceSq - bestDistanceSq) <= 1e-12f;
				bool better = _envelope ? BetterEnvelope(candidateHeight, currentHeight, _sources[sourceIndex], bestSource < 0 ? null : _sources[bestSource]) : Better(_sources[sourceIndex], bestSource < 0 ? null : _sources[bestSource]);
				if (closer || tied && better)
				{
					bestDistanceSq = distanceSq;
					bestSource = sourceIndex;
					bestWeights = weights;
				}
			}

			float maximumFallbackDistance = (float)(8.0 / SweepRibbonPolygonUnion.Scale);
			if (bestSource < 0 || bestDistanceSq > maximumFallbackDistance * maximumFallbackDistance)
			{
				failure = "GlobalSourceMissing";
				return false;
			}

			SweepRibbonSourceTriangle nearest = _sources[bestSource];
			height = bestWeights.x * nearest.WorldA.y + bestWeights.y * nearest.WorldB.y + bestWeights.z * nearest.WorldC.y;
			uv = bestWeights.x * nearest.UvA + bestWeights.y * nearest.UvB + bestWeights.z * nearest.UvC;
			return true;
		}

		private void BuildIndex()
		{
			for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
			{
				SweepRibbonSourceTriangle source = _sources[sourceIndex];
				float2 minimum = math.min(source.A, math.min(source.B, source.C));
				float2 maximum = math.max(source.A, math.max(source.B, source.C));
				int x0 = Cell(minimum.x);
				int x1 = Cell(maximum.x);
				int y0 = Cell(minimum.y);
				int y1 = Cell(maximum.y);
				long count = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
				if (count > MaxCellsPerTriangle)
				{
					_large.Add(sourceIndex);
					continue;
				}
				for (int x = x0; x <= x1; x++)
				{
					for (int y = y0; y <= y1; y++)
					{
						var key = (x, y);
						if (!_cells.TryGetValue(key, out List<int> cell))
						{
							cell = new List<int>();
							_cells.Add(key, cell);
						}
						cell.Add(sourceIndex);
					}
				}
			}
		}

		private List<int> Candidates(float2 point)
		{
			var result = new List<int>();
			var key = (Cell(point.x), Cell(point.y));
			if (_cells.TryGetValue(key, out List<int> cell))
				result.AddRange(cell);
			result.AddRange(_large);
			return result;
		}

		private int Cell(float coordinate)
		{
			return (int)math.floor(coordinate / _cellSize);
		}

		private static bool TryBarycentric(SweepRibbonSourceTriangle source, float2 point, float tolerance, out float3 weights)
		{
			float2 ab = source.B - source.A;
			float2 ac = source.C - source.A;
			float denominator = Cross(ab, ac);
			if (math.abs(denominator) <= 1e-12f)
			{
				weights = default;
				return false;
			}
			float2 ap = point - source.A;
			float v = Cross(ap, ac) / denominator;
			float w = Cross(ab, ap) / denominator;
			float u = 1f - v - w;
			weights = new float3(u, v, w);
			return u >= -tolerance && v >= -tolerance && w >= -tolerance;
		}

		private static void ClosestWeights(SweepRibbonSourceTriangle source, float2 point, out float3 weights, out float distanceSq)
		{
			if (TryBarycentric(source, point, 0f, out weights))
			{
				distanceSq = 0f;
				return;
			}
			Project(point, source.A, source.B, out float ab, out float abDistanceSq);
			Project(point, source.B, source.C, out float bc, out float bcDistanceSq);
			Project(point, source.C, source.A, out float ca, out float caDistanceSq);
			if (abDistanceSq <= bcDistanceSq && abDistanceSq <= caDistanceSq)
			{
				weights = new float3(1f - ab, ab, 0f);
				distanceSq = abDistanceSq;
				return;
			}
			if (bcDistanceSq <= caDistanceSq)
			{
				weights = new float3(0f, 1f - bc, bc);
				distanceSq = bcDistanceSq;
				return;
			}
			weights = new float3(ca, 0f, 1f - ca);
			distanceSq = caDistanceSq;
		}

		private static void Project(float2 point, float2 a, float2 b, out float t, out float distanceSq)
		{
			float2 ab = b - a;
			float lengthSq = math.dot(ab, ab);
			t = lengthSq > 1e-12f ? math.saturate(math.dot(point - a, ab) / lengthSq) : 0f;
			distanceSq = math.distancesq(point, a + ab * t);
		}

		private static bool Better(SweepRibbonSourceTriangle candidate, SweepRibbonSourceTriangle current)
		{
			return current == null || candidate.Strip && !current.Strip || candidate.Strip == current.Strip && candidate.SourceOrder < current.SourceOrder;
		}

		private bool BetterEnvelope(float candidateHeight, float currentHeight, SweepRibbonSourceTriangle candidate, SweepRibbonSourceTriangle current)
		{
			if (current == null)
				return true;
			float difference = candidateHeight - currentHeight;
			if (math.abs(difference) > 1e-6f)
				return _minimumEnvelope ? difference < 0f : difference > 0f;
			return Better(candidate, current);
		}

		private static string HeightConflictFailure(float2 point, float difference, SweepRibbonSourceTriangle selected, float selectedHeight, SweepRibbonSourceTriangle minimum, float minimumHeight, SweepRibbonSourceTriangle maximum, float maximumHeight)
		{
			return "GlobalHeightConflict-" + Format(difference) +
				"-Point-" + Format(point.x) + "_" + Format(point.y) +
				"-Selected-" + Source(selected, selectedHeight) +
				"-Minimum-" + Source(minimum, minimumHeight) +
				"-Maximum-" + Source(maximum, maximumHeight);
		}

		private static string Source(SweepRibbonSourceTriangle source, float height)
		{
			return source == null
				? "None"
				: source.SourceOrder + "_" + (source.Strip ? "Strip" : "Approach") + "_Component_" + source.NetworkComponent + "_Height_" + Format(height);
		}

		private static string Format(float value)
		{
			return value.ToString("F5", CultureInfo.InvariantCulture);
		}

		private static float Cross(float2 first, float2 second)
		{
			return first.x * second.y - first.y * second.x;
		}
	}
}
