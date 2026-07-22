using System.Collections.Generic;
using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepJunctionInterpolator
	{
		private readonly List<float2> _boundary;
		private readonly List<float> _heights;
		private readonly List<float> _rvs;
		private readonly int _n;
		private readonly float2[] _rel;
		private readonly float[] _dist;
		private readonly float[] _tan;

		internal SweepJunctionInterpolator(List<float2> boundary, List<float> heights, List<float> rvs)
		{
			_boundary = boundary;
			_heights = heights;
			_rvs = rvs;
			_n = boundary.Count;
			_rel = new float2[_n];
			_dist = new float[_n];
			_tan = new float[_n];
		}

		internal void Sample(float2 p, out float height, out float rv)
		{
			int n = _n;

			for (int i = 0; i < n; i++)
			{
				float2 rel = _boundary[i] - p;
				float d = math.length(rel);
				if (d < 1e-6f)
				{
					height = _heights[i];
					rv = _rvs[i];
					return;
				}
				_rel[i] = rel;
				_dist[i] = d;
			}

			for (int i = 0; i < n; i++)
			{
				int j = (i + 1) % n;
				float2 a = _rel[i];
				float2 b = _rel[j];
				float denom = _dist[i] * _dist[j] + math.dot(a, b);
				float cross = a.x * b.y - a.y * b.x;
				if (math.abs(denom) < 1e-9f)
				{
					float2 e0 = _boundary[i];
					float2 e1 = _boundary[j];
					float2 ed = e1 - e0;
					float len2 = math.dot(ed, ed);
					float t = len2 > 1e-12f ? math.saturate(math.dot(p - e0, ed) / len2) : 0f;
					height = math.lerp(_heights[i], _heights[j], t);
					rv = math.lerp(_rvs[i], _rvs[j], t);
					return;
				}
				_tan[i] = cross / denom;
			}

			float sumW = 0f;
			float sumH = 0f;
			float sumR = 0f;
			for (int i = 0; i < n; i++)
			{
				int prev = (i - 1 + n) % n;
				float w = (_tan[prev] + _tan[i]) / _dist[i];
				sumW += w;
				sumH += w * _heights[i];
				sumR += w * _rvs[i];
			}

			if (math.abs(sumW) < 1e-12f)
			{
				height = _heights[0];
				rv = _rvs[0];
				return;
			}

			height = sumH / sumW;
			rv = sumR / sumW;
		}
	}
}
