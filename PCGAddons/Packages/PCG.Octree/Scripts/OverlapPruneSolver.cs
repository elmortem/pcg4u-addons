using System.Collections.Generic;
using System.Threading;
using Octree;
using PCG.Points;
using Unity.Mathematics;

namespace PCG.Octree
{
	public static class OverlapPruneSolver
	{
		public const int PortCount = 4;

		public static PcgPointCloud[] Prune(PcgPointCloud[][] ports, float[] radii, bool[] selfPrune, float overlap, CancellationToken ct)
		{
			var outputs = new PcgPointCloud[PortCount];
			for (int p = 0; p < PortCount; p++)
				outputs[p] = new PcgPointCloud();

			var candidates = new List<PruneCandidate>();
			float maxRadius = 0f;
			var min = new float2(float.MaxValue, float.MaxValue);
			var max = new float2(float.MinValue, float.MinValue);

			for (int p = 0; p < PortCount && p < ports.Length; p++)
			{
				if (ports[p] == null)
					continue;

				foreach (var cloud in ports[p])
				{
					if (cloud == null)
						continue;

					for (int i = 0; i < cloud.Count; i++)
					{
						var point = cloud[i];
						float radius = radii[p] * point.Scale;
						candidates.Add(new PruneCandidate
						{
							Position = point.Position,
							Radius = radius,
							Port = p,
							Cloud = cloud,
							Index = i
						});
						maxRadius = math.max(maxRadius, radius);
						min = math.min(min, new float2(point.Position.x, point.Position.z));
						max = math.max(max, new float2(point.Position.x, point.Position.z));
					}
				}
			}

			if (candidates.Count == 0)
				return outputs;

			var order = new List<int>(candidates.Count);
			for (int i = 0; i < candidates.Count; i++)
				order.Add(i);

			order.Sort((x, y) =>
			{
				int byPort = candidates[x].Port.CompareTo(candidates[y].Port);
				if (byPort != 0)
					return byPort;

				int byRadius = candidates[y].Radius.CompareTo(candidates[x].Radius);
				if (byRadius != 0)
					return byRadius;

				return x.CompareTo(y);
			});

			var extent = max - min;
			float worldSize = math.max(1f, math.max(extent.x, extent.y) + maxRadius * 4f + 1f);
			var center = (min + max) * 0.5f;
			float nodeSize = math.min(worldSize, math.max(0.5f, worldSize / math.sqrt(candidates.Count) * 2.5f));
			var octree = new PointOctree<int>(worldSize, new float3(center.x, 0f, center.y), nodeSize);

			var accepted = new bool[candidates.Count];
			var buffer = new List<int>();

			foreach (int id in order)
			{
				ct.ThrowIfCancellationRequested();
				var cand = candidates[id];
				var flatPos = new float3(cand.Position.x, 0f, cand.Position.z);
				float maxDist = overlap * (cand.Radius + maxRadius) + 0.001f;

				buffer.Clear();
				octree.GetNearbyNonAlloc(flatPos, maxDist, buffer);

				bool conflict = false;
				foreach (int otherId in buffer)
				{
					var other = candidates[otherId];
					if (other.Port == cand.Port && !selfPrune[cand.Port])
						continue;

					var d = new float2(cand.Position.x - other.Position.x, cand.Position.z - other.Position.z);
					float threshold = overlap * (cand.Radius + other.Radius);
					if (math.lengthsq(d) < threshold * threshold)
					{
						conflict = true;
						break;
					}
				}

				if (!conflict)
				{
					accepted[id] = true;
					octree.Add(id, flatPos);
				}
			}

			for (int i = 0; i < candidates.Count; i++)
			{
				if (!accepted[i])
					continue;

				var cand = candidates[i];
				outputs[cand.Port].AppendFrom(cand.Cloud, cand.Index);
			}

			return outputs;
		}
	}
}
