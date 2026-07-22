using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Sweep
{
	internal static class SweepRibbonSplitter
	{
		private const float MinFreeLength = 0.5f;
		private const int CrossSamples = 5;
		private const int GuardSamples = 1;

		private const int StateGreen = 0;
		private const int StateRed = 1;
		private const int StateBlue = 2;

		private struct Sample
		{
			public float3 Center;
			public float2 LeftPlan;
			public float2 RightPlan;
			public float3 LeftWorld;
			public float3 RightWorld;
			public float Station;
		}

		private struct Quad
		{
			public int Spline;
			public int Base;
			public float Station;
			public float2 A;
			public float2 B;
			public float2 C;
			public float2 D;
			public float YA;
			public float YB;
			public float YC;
			public float YD;
		}

		internal static bool CanBuild(SweepSnapshot snapshot, out string failure)
		{
			failure = null;

			if (snapshot.ProfileClosed || snapshot.ProfilePoints.Length != 2 || snapshot.ProfileSegments.Length != 2)
			{
				failure = "SplitRibbonProfileRequired";
				return false;
			}

			float2 a = snapshot.ProfilePoints[0];
			float2 b = snapshot.ProfilePoints[1];
			float tolerance = math.max(1e-6f, math.abs(b.x - a.x) * 1e-5f);
			if (math.abs(a.y) > tolerance || math.abs(b.y) > tolerance || math.abs(a.x - b.x) <= tolerance)
			{
				failure = "SplitRibbonProfileRequired";
				return false;
			}

			return true;
		}

		internal static SweepRibbonSplitResult Split(SweepSnapshot full, List<Spline> splines, float step, float thickness, CancellationToken ct, Action reportProgress)
		{
			int splineCount = splines.Count;
			var result = new SweepRibbonSplitResult();

			float profileHalf = math.max(math.abs(full.ProfilePoints[0].x), math.abs(full.ProfilePoints[1].x));
			float planWidth = math.max(1e-3f, profileHalf * 2f * MaxLut(full.WidthLut));
			float verticalTolerance = math.max(0f, thickness);

			var samples = new Sample[splineCount][];
			for (int i = 0; i < splineCount; i++)
			{
				ct.ThrowIfCancellationRequested();
				reportProgress();
				samples[i] = SampleSpline(splines[i], step, profileHalf, full.WidthLut);
			}

			var quads = new List<Quad>();
			for (int i = 0; i < splineCount; i++)
			{
				var arr = samples[i];
				for (int r = 0; r + 1 < arr.Length; r++)
				{
					var quad = new Quad
					{
						Spline = i,
						Base = r,
						Station = (arr[r].Station + arr[r + 1].Station) * 0.5f,
						A = arr[r].LeftPlan,
						B = arr[r + 1].LeftPlan,
						C = arr[r + 1].RightPlan,
						D = arr[r].RightPlan,
						YA = arr[r].LeftWorld.y,
						YB = arr[r + 1].LeftWorld.y,
						YC = arr[r + 1].RightWorld.y,
						YD = arr[r].RightWorld.y
					};

					float2 min = math.min(math.min(quad.A, quad.B), math.min(quad.C, quad.D));
					float2 max = math.max(math.max(quad.A, quad.B), math.max(quad.C, quad.D));
					if (math.all(max - min < 1e-7f))
						continue;

					quads.Add(quad);
				}
			}

			float cellSize = math.max(0.05f, planWidth);
			var grid = new Dictionary<long, List<int>>();
			for (int q = 0; q < quads.Count; q++)
				InsertQuad(grid, quads[q], cellSize, q);

			var candidates = new List<int>();

			var dirty = new bool[splineCount][];
			var touches = new List<int>[splineCount][];
			var pieceId = new int[splineCount][];

			for (int i = 0; i < splineCount; i++)
			{
				var arr = samples[i];
				int count = arr.Length;
				dirty[i] = new bool[count];
				touches[i] = new List<int>[count];
				pieceId[i] = new int[count];

				for (int r = 0; r < count; r++)
				{
					pieceId[i][r] = -1;
					touches[i][r] = FindTouches(arr[r], i, r, grid, quads, candidates, cellSize, GuardSamples, verticalTolerance);
					dirty[i][r] = touches[i][r].Count > 0;
				}
			}

			for (int i = 0; i < splineCount; i++)
			{
				var list = touches[i];
				for (int r = 0; r < list.Length; r++)
				{
					var t = list[r];
					for (int k = 0; k < t.Count; k++)
					{
						var quad = quads[t[k]];
						dirty[quad.Spline][quad.Base] = true;
						if (quad.Base + 1 < dirty[quad.Spline].Length)
							dirty[quad.Spline][quad.Base + 1] = true;
					}
				}
			}

			int pieceCount = 0;
			for (int i = 0; i < splineCount; i++)
			{
				var d = dirty[i];
				int r = 0;
				while (r < d.Length)
				{
					if (!d[r])
					{
						r++;
						continue;
					}

					int id = pieceCount++;
					while (r < d.Length && d[r])
					{
						pieceId[i][r] = id;
						r++;
					}
				}
			}

			var pieceTouchesOther = new bool[pieceCount];
			for (int i = 0; i < splineCount; i++)
			{
				var d = dirty[i];
				for (int r = 0; r < d.Length; r++)
				{
					if (!d[r])
						continue;

					int p = pieceId[i][r];
					if (pieceTouchesOther[p])
						continue;

					var t = touches[i][r];
					for (int k = 0; k < t.Count; k++)
					{
						var quad = quads[t[k]];
						int op = pieceId[quad.Spline][quad.Base];
						if (op >= 0 && op != p)
						{
							pieceTouchesOther[p] = true;
							break;
						}
					}
				}
			}

			for (int i = 0; i < splineCount; i++)
			{
				ct.ThrowIfCancellationRequested();
				reportProgress();

				var arr = samples[i];
				int count = arr.Length;
				if (count < 2)
					continue;

				var state = new int[count];
				for (int r = 0; r < count; r++)
				{
					if (!dirty[i][r])
					{
						state[r] = StateGreen;
					}
					else
					{
						state[r] = pieceTouchesOther[pieceId[i][r]] ? StateRed : StateBlue;
					}

					result.DebugCuts.Add(new[] { (Vector3)arr[r].LeftWorld, (Vector3)arr[r].RightWorld });
					result.DebugState.Add(state[r]);
				}

				int p = 0;
				while (p < count)
				{
					int runStart = p;
					int value = state[p];
					while (p < count && state[p] == value)
						p++;
					int runEnd = p - 1;

					float startStation;
					float endStation;
					if (value == StateGreen)
					{
						startStation = runStart == 0 ? arr[0].Station : arr[runStart - 1].Station;
						endStation = p >= count ? arr[count - 1].Station : arr[p].Station;
					}
					else
					{
						startStation = arr[runStart].Station;
						endStation = arr[runEnd].Station;
					}

					result.Pieces.Add(new SweepRibbonPiece
					{
						Spline = i,
						StartStation = startStation,
						EndStation = endStation,
						State = value
					});
				}

				EmitSpline(arr, state, result);
			}

			return result;
		}

		private static List<int> FindTouches(Sample sample, int splineIndex, int sampleIndex, Dictionary<long, List<int>> grid, List<Quad> quads, List<int> candidates, float cellSize, int guardSamples, float verticalTolerance)
		{
			var hits = new List<int>();
			CollectCandidates(grid, sample.LeftPlan, sample.RightPlan, cellSize, candidates);

			for (int c = 0; c < candidates.Count; c++)
			{
				int qi = candidates[c];
				var quad = quads[qi];
				if (quad.Spline == splineIndex && quad.Base >= sampleIndex - 1 - guardSamples && quad.Base <= sampleIndex + guardSamples)
					continue;

				bool inside = false;
				for (int s = 0; s < CrossSamples && !inside; s++)
				{
					float f = s / (float)(CrossSamples - 1);
					float2 point = math.lerp(sample.LeftPlan, sample.RightPlan, f);
					if (PointInQuad(point, quad, out float quadY))
					{
						float cutY = math.lerp(sample.LeftWorld.y, sample.RightWorld.y, f);
						if (math.abs(cutY - quadY) <= verticalTolerance)
							inside = true;
					}
				}

				if (inside && !hits.Contains(qi))
					hits.Add(qi);
			}

			return hits;
		}

		private static Sample[] SampleSpline(Spline spline, float baseStep, float profileHalf, float[] widthLut)
		{
			float length = spline.GetLength();
			if (!(length > 1e-4f))
				return Array.Empty<Sample>();

			var dists = SweepRibbonSampling.AdaptiveStations(spline, 0f, length, baseStep);
			int total = dists.Count;

			var positions = new float3[total];
			var ts = new float[total];
			for (int q = 0; q < total; q++)
			{
				float t = math.saturate(spline.ConvertIndexUnit(dists[q], PathIndexUnit.Distance, PathIndexUnit.Normalized));
				ts[q] = t;
				positions[q] = spline.EvaluatePosition(t);
			}

			var samples = new Sample[total];
			for (int q = 0; q < total; q++)
			{
				int prev = math.max(0, q - 1);
				int next = math.min(total - 1, q + 1);
				float3 tangent = spline.EvaluateTangent(ts[q]);
				float3 up = spline.EvaluateUpVector(ts[q]);
				float3 right3 = SweepRibbonSampling.Right3D(tangent, up, positions[prev], positions[next]);
				float halfWidth = profileHalf * SampleLut(widthLut, ts[q]);

				float3 center = positions[q];
				float3 leftWorld = center + right3 * halfWidth;
				float3 rightWorld = center - right3 * halfWidth;

				samples[q] = new Sample
				{
					Center = center,
					LeftPlan = new float2(leftWorld.x, leftWorld.z),
					RightPlan = new float2(rightWorld.x, rightWorld.z),
					LeftWorld = leftWorld,
					RightWorld = rightWorld,
					Station = dists[q]
				};
			}

			return samples;
		}

		private static void EmitSpline(Sample[] arr, int[] state, SweepRibbonSplitResult result)
		{
			int count = arr.Length;

			for (int r = 1; r < count; r++)
			{
				bool greenPrev = state[r - 1] == StateGreen;
				bool greenCur = state[r] == StateGreen;
				if (greenPrev != greenCur)
				{
					int boundary = greenCur ? r - 1 : r;
					result.CutChords.Add(new[] { (Vector3)arr[boundary].LeftWorld, (Vector3)arr[boundary].RightWorld });
				}
			}

			int start = 0;
			while (start < count)
			{
				if (state[start] != StateGreen)
				{
					start++;
					continue;
				}

				int end = start;
				while (end + 1 < count && state[end + 1] == StateGreen)
					end++;

				if (arr[end].Station - arr[start].Station > MinFreeLength)
				{
					var polyline = new Vector3[end - start + 1];
					for (int r = start; r <= end; r++)
						polyline[r - start] = arr[r].Center;
					result.FreeSplines.Add(polyline);
				}

				start = end + 1;
			}
		}

		private static void InsertQuad(Dictionary<long, List<int>> grid, Quad quad, float cellSize, int index)
		{
			float2 min = math.min(math.min(quad.A, quad.B), math.min(quad.C, quad.D));
			float2 max = math.max(math.max(quad.A, quad.B), math.max(quad.C, quad.D));

			int x0 = (int)math.floor(min.x / cellSize);
			int x1 = (int)math.floor(max.x / cellSize);
			int y0 = (int)math.floor(min.y / cellSize);
			int y1 = (int)math.floor(max.y / cellSize);

			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					long key = ((long)x << 32) ^ (uint)y;
					if (!grid.TryGetValue(key, out var list))
					{
						list = new List<int>();
						grid.Add(key, list);
					}
					list.Add(index);
				}
			}
		}

		private static void CollectCandidates(Dictionary<long, List<int>> grid, float2 a, float2 b, float cellSize, List<int> candidates)
		{
			candidates.Clear();
			int x0 = (int)math.floor(math.min(a.x, b.x) / cellSize) - 1;
			int x1 = (int)math.floor(math.max(a.x, b.x) / cellSize) + 1;
			int y0 = (int)math.floor(math.min(a.y, b.y) / cellSize) - 1;
			int y1 = (int)math.floor(math.max(a.y, b.y) / cellSize) + 1;

			for (int x = x0; x <= x1; x++)
			{
				for (int y = y0; y <= y1; y++)
				{
					long key = ((long)x << 32) ^ (uint)y;
					if (grid.TryGetValue(key, out var list))
						candidates.AddRange(list);
				}
			}
		}

		private static bool PointInQuad(float2 p, Quad quad, out float y)
		{
			if (PointInTriangle(p, quad.A, quad.B, quad.C, quad.YA, quad.YB, quad.YC, out y))
				return true;

			return PointInTriangle(p, quad.A, quad.C, quad.D, quad.YA, quad.YC, quad.YD, out y);
		}

		private static bool PointInTriangle(float2 p, float2 a, float2 b, float2 c, float ya, float yb, float yc, out float y)
		{
			y = 0f;

			float2 v0 = b - a;
			float2 v1 = c - a;
			float2 v2 = p - a;
			float den = v0.x * v1.y - v1.x * v0.y;
			if (math.abs(den) < 1e-12f)
				return false;

			float v = (v2.x * v1.y - v1.x * v2.y) / den;
			float w = (v0.x * v2.y - v2.x * v0.y) / den;
			float u = 1f - v - w;
			if (u < -1e-4f || v < -1e-4f || w < -1e-4f)
				return false;

			y = u * ya + v * yb + w * yc;
			return true;
		}

		private static float SampleLut(float[] lut, float t)
		{
			float f = math.saturate(t) * (lut.Length - 1);
			int i0 = (int)math.floor(f);
			int i1 = math.min(i0 + 1, lut.Length - 1);
			return math.lerp(lut[i0], lut[i1], f - i0);
		}

		private static float MaxLut(float[] lut)
		{
			float max = lut[0];
			for (int i = 1; i < lut.Length; i++)
				max = math.max(max, lut[i]);
			return max;
		}
	}
}
