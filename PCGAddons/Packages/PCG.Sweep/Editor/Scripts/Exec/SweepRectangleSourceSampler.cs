using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepRectangleSourceSampler
	{
		private const int MaxCellsPerTriangle = 4096;
		private const float BarycentricTolerance = 1e-4f;
		private readonly SweepRectangleSourceTriangle[] _sources;
		private readonly Dictionary<(int, int), List<int>> _cells = new Dictionary<(int, int), List<int>>();
		private readonly List<int> _large = new List<int>();
		private readonly float _cellSize;
		private readonly float _heightTolerance;

		private SweepRectangleSourceSampler(SweepRectangleSourceTriangle[] sources, float cellSize, float heightTolerance)
		{
			_sources = sources;
			_cellSize = math.max(0.05f, cellSize);
			_heightTolerance = math.max(1e-4f, heightTolerance);
			BuildIndex();
		}

		internal static bool TryCreate(
			SweepRibbonSourceTriangle[] bottomSources,
			SweepRectangleSourceTriangle[] allSources,
			float cellSize,
			float heightTolerance,
			out SweepRectangleSourceSampler sampler,
			out string failure)
		{
			sampler = null;
			failure = null;
			if (bottomSources == null || bottomSources.Length == 0 || allSources == null)
			{
				failure = "RectangleSamplerSourcesMissing";
				return false;
			}
			var sources = new SweepRectangleSourceTriangle[bottomSources.Length];
			for (int index = 0; index < bottomSources.Length; index++)
			{
				int sourceOrder = bottomSources[index].SourceOrder;
				if (sourceOrder < 0 || sourceOrder >= allSources.Length)
				{
					failure = "RectangleSamplerSourceInvalid-" + sourceOrder;
					return false;
				}
				sources[index] = allSources[sourceOrder];
			}
			sampler = new SweepRectangleSourceSampler(sources, cellSize, heightTolerance);
			return true;
		}

		internal bool TrySample(
			float2 point,
			out float3 bottom,
			out float3 top,
			out float2 bottomUv,
			out float2 topUv,
			out string failure)
		{
			bottom = default;
			top = default;
			bottomUv = default;
			topUv = default;
			failure = null;
			List<int> candidates = Candidates(point);
			bool found = false;
			bool hasApproach = false;
			bool selectedStrip = false;
			int selectedOrder = int.MaxValue;
			SweepRectangleSourceTriangle selected = null;
			float3 selectedWeights = default;
			float minimumBottom = float.MaxValue;
			float maximumBottom = float.MinValue;
			float minimumTop = float.MaxValue;
			float maximumTop = float.MinValue;
			for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
			{
				SweepRectangleSourceTriangle source = _sources[candidates[candidateIndex]];
				if (!TryBarycentric(source, point, BarycentricTolerance, out float3 weights))
					continue;
				float bottomHeight = Interpolate(source.BottomA.y, source.BottomB.y, source.BottomC.y, weights);
				float topHeight = Interpolate(source.TopA.y, source.TopB.y, source.TopC.y, weights);
				minimumBottom = math.min(minimumBottom, bottomHeight);
				maximumBottom = math.max(maximumBottom, bottomHeight);
				minimumTop = math.min(minimumTop, topHeight);
				maximumTop = math.max(maximumTop, topHeight);
				hasApproach |= !source.Strip;
				bool better = !found || source.Strip && !selectedStrip || source.Strip == selectedStrip && source.SourceOrder < selectedOrder;
				if (better)
				{
					found = true;
					selectedStrip = source.Strip;
					selectedOrder = source.SourceOrder;
					selected = source;
					selectedWeights = weights;
				}
			}

			if (found)
			{
				if (!hasApproach && maximumBottom - minimumBottom > _heightTolerance)
				{
					failure = HeightConflict("RectangleBottomHeightConflict", point, maximumBottom - minimumBottom);
					return false;
				}
				if (!hasApproach && maximumTop - minimumTop > _heightTolerance)
				{
					failure = HeightConflict("RectangleTopHeightConflict", point, maximumTop - minimumTop);
					return false;
				}
				Interpolate(selected, selectedWeights, out bottom, out top, out bottomUv, out topUv);
				return true;
			}

			float bestDistanceSq = float.MaxValue;
			int bestSource = -1;
			float3 bestWeights = default;
			for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
			{
				ClosestWeights(_sources[sourceIndex], point, out float3 weights, out float distanceSq);
				if (distanceSq < bestDistanceSq || math.abs(distanceSq - bestDistanceSq) <= 1e-12f && Better(_sources[sourceIndex], bestSource < 0 ? null : _sources[bestSource]))
				{
					bestDistanceSq = distanceSq;
					bestSource = sourceIndex;
					bestWeights = weights;
				}
			}

			float maximumFallbackDistance = (float)(8.0 / SweepRibbonPolygonUnion.Scale);
			if (bestSource < 0 || bestDistanceSq > maximumFallbackDistance * maximumFallbackDistance)
			{
				failure = "RectangleSourceMissing";
				return false;
			}
			Interpolate(_sources[bestSource], bestWeights, out bottom, out top, out bottomUv, out topUv);
			return true;
		}

		private void BuildIndex()
		{
			for (int sourceIndex = 0; sourceIndex < _sources.Length; sourceIndex++)
			{
				SweepRectangleSourceTriangle source = _sources[sourceIndex];
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

		private static bool TryBarycentric(SweepRectangleSourceTriangle source, float2 point, float tolerance, out float3 weights)
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

		private static void ClosestWeights(SweepRectangleSourceTriangle source, float2 point, out float3 weights, out float distanceSq)
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

		private static bool Better(SweepRectangleSourceTriangle candidate, SweepRectangleSourceTriangle current)
		{
			return current == null || candidate.Strip && !current.Strip || candidate.Strip == current.Strip && candidate.SourceOrder < current.SourceOrder;
		}

		private static void Interpolate(
			SweepRectangleSourceTriangle source,
			float3 weights,
			out float3 bottom,
			out float3 top,
			out float2 bottomUv,
			out float2 topUv)
		{
			bottom = weights.x * source.BottomA + weights.y * source.BottomB + weights.z * source.BottomC;
			top = weights.x * source.TopA + weights.y * source.TopB + weights.z * source.TopC;
			bottomUv = weights.x * source.BottomUvA + weights.y * source.BottomUvB + weights.z * source.BottomUvC;
			topUv = weights.x * source.TopUvA + weights.y * source.TopUvB + weights.z * source.TopUvC;
		}

		private static float Interpolate(float a, float b, float c, float3 weights)
		{
			return weights.x * a + weights.y * b + weights.z * c;
		}

		private static string HeightConflict(string code, float2 point, float difference)
		{
			return code + "-" + Format(difference) + "-Point-" + Format(point.x) + "_" + Format(point.y);
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
