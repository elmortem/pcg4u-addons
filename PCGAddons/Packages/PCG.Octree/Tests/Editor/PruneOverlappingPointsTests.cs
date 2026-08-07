using System.Threading;
using NUnit.Framework;
using PCG.Octree;
using PCG.Points;
using Unity.Mathematics;

namespace PCG.Octree.Tests
{
	public class PruneOverlappingPointsTests
	{
		private const string Tag = "tag";

		private static readonly bool[] DefaultSelfPrune = { false, true, true, true };

		private static PcgPointCloud Cloud(params float3[] positions)
		{
			var cloud = new PcgPointCloud();
			foreach (var position in positions)
			{
				cloud.Points.Add(new PointData
				{
					Position = position,
					Normal = new float3(0f, 1f, 0f),
					Scale = 1f
				});
				cloud.Attributes.AddRow();
			}

			return cloud;
		}

		private static void SetTags(PcgPointCloud cloud, params int[] tags)
		{
			for (int i = 0; i < tags.Length; i++)
				cloud.Attributes.Set(Tag, i, tags[i]);
		}

		[Test]
		public void CrossLayerOverlap_RemovesWeakerKeepsStronger()
		{
			var ports = new[]
			{
				new[] { Cloud(new float3(0f, 0f, 0f)) },
				new[] { Cloud(new float3(1f, 0f, 0f)) },
				null,
				null
			};
			var radii = new[] { 2f, 2f, 1f, 1f };

			var outputs = OverlapPruneSolver.Prune(ports, radii, DefaultSelfPrune, 0.9f, CancellationToken.None);

			Assert.AreEqual(1, outputs[0].Count);
			Assert.AreEqual(0, outputs[1].Count);
		}

		[Test]
		public void NonOverlappingLayers_AllKept()
		{
			var ports = new[]
			{
				new[] { Cloud(new float3(0f, 0f, 0f)) },
				new[] { Cloud(new float3(10f, 0f, 0f)) },
				null,
				null
			};
			var radii = new[] { 2f, 2f, 1f, 1f };

			var outputs = OverlapPruneSolver.Prune(ports, radii, DefaultSelfPrune, 0.9f, CancellationToken.None);

			Assert.AreEqual(1, outputs[0].Count);
			Assert.AreEqual(1, outputs[1].Count);
		}

		[Test]
		public void SelfPrune_ControlsConflictsInsideLayer()
		{
			var radii = new[] { 1f, 1f, 1f, 1f };

			var pruned = OverlapPruneSolver.Prune(new[]
			{
				null,
				new[] { Cloud(new float3(0f, 0f, 0f), new float3(1f, 0f, 0f)) },
				null,
				null
			}, radii, new[] { false, true, true, true }, 0.9f, CancellationToken.None);
			Assert.AreEqual(1, pruned[1].Count);

			var kept = OverlapPruneSolver.Prune(new[]
			{
				null,
				new[] { Cloud(new float3(0f, 0f, 0f), new float3(1f, 0f, 0f)) },
				null,
				null
			}, radii, new[] { false, false, true, true }, 0.9f, CancellationToken.None);
			Assert.AreEqual(2, kept[1].Count);
		}

		[Test]
		public void AttributesAndPortLayout_ArePreserved()
		{
			var layer1 = Cloud(new float3(0f, 0f, 0f), new float3(20f, 0f, 0f));
			SetTags(layer1, 7, 8);
			var layer2 = Cloud(new float3(0.5f, 50f, 0f));
			SetTags(layer2, 9);
			var layer3 = Cloud(new float3(40f, 0f, 0f));
			SetTags(layer3, 11);

			var ports = new[]
			{
				null,
				new[] { layer1 },
				new[] { layer2 },
				new[] { layer3 }
			};
			var radii = new[] { 1f, 1f, 1f, 1f };

			var outputs = OverlapPruneSolver.Prune(ports, radii, DefaultSelfPrune, 0.9f, CancellationToken.None);

			Assert.AreEqual(0, outputs[0].Count);
			Assert.AreEqual(2, outputs[1].Count);
			Assert.AreEqual(0, outputs[2].Count);
			Assert.AreEqual(1, outputs[3].Count);
			Assert.AreEqual(7, outputs[1].Attributes.Get<int>(Tag, 0));
			Assert.AreEqual(8, outputs[1].Attributes.Get<int>(Tag, 1));
			Assert.AreEqual(11, outputs[3].Attributes.Get<int>(Tag, 0));
		}
	}
}
