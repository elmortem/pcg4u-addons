using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using PCG.Polygons;
using PCG.Polygons.City;
using Unity.Mathematics;

namespace PCG.Polygons.Tests
{
	public class LotFrontagePointsTests
	{
		private static LotFrontageSettings DefaultSettings(int seed = 0, float jitter = 0f)
		{
			return new LotFrontageSettings
			{
				Setback = 4f,
				MaxRoadDistance = 7f,
				MinFrontage = 6f,
				SetbackJitter = jitter,
				Seed = seed,
				MinPlacementClearance = 2f,
				MaxPlacementDistance = 9f
			};
		}

		private static float2[] Rect(float xMin, float zMin, float xMax, float zMax)
		{
			return new[]
			{
				new float2(xMin, zMin),
				new float2(xMax, zMin),
				new float2(xMax, zMax),
				new float2(xMin, zMax)
			};
		}

		private static RegionSet MakeSet(params float2[][] outers)
		{
			var set = new RegionSet();
			foreach (var outer in outers)
				set.AddRegion(new Polygon2D { Outer = outer });

			return set;
		}

		[Test]
		public void SquareLotNearRoad_PlacesPointWithSetbackAndAttributes()
		{
			var lots = MakeSet(Rect(0f, 0f, 10f, 10f));
			var roads = MakeSet(Rect(-9f, -20f, -4f, 20f));

			var cloud = LotFrontage.Build(lots, roads, DefaultSettings(), CancellationToken.None);

			Assert.AreEqual(1, cloud.Count);
			var point = cloud[0];
			Assert.AreEqual(4f, point.Position.x, 1e-3f);
			Assert.AreEqual(5f, point.Position.z, 1e-3f);
			Assert.AreEqual(-90f, point.Angle, 1e-2f);
			Assert.AreEqual(0, cloud.Attributes.Get<int>(CityAttributes.LotId, 0));
			Assert.AreEqual(100f, cloud.Attributes.Get<float>(CityAttributes.LotArea, 0), 1e-2f);
			Assert.AreEqual(10f, cloud.Attributes.Get<float>(CityAttributes.LotWidth, 0), 1e-3f);
			Assert.AreEqual(0, cloud.Attributes.Get<int>(CityAttributes.RoadClass, 0));
		}

		[Test]
		public void CornerLot_TieWithinTolerance_PicksLongerEdge()
		{
			var lots = MakeSet(Rect(0f, 0f, 10f, 6f));
			var roads = MakeSet(
				Rect(-9f, -20f, -4f, 20f),
				Rect(-20f, -9.3f, 20f, -4.3f));

			var cloud = LotFrontage.Build(lots, roads, DefaultSettings(), CancellationToken.None);

			Assert.AreEqual(1, cloud.Count);
			var point = cloud[0];
			Assert.AreEqual(5f, point.Position.x, 1e-3f);
			Assert.AreEqual(4f, point.Position.z, 1e-3f);
			Assert.AreEqual(180f, math.abs(point.Angle), 1e-2f);
			Assert.AreEqual(10f, cloud.Attributes.Get<float>(CityAttributes.LotWidth, 0), 1e-3f);
		}

		[Test]
		public void LotWithoutRoadAccess_IsSkipped()
		{
			var lots = MakeSet(Rect(0f, 0f, 10f, 10f));
			var roads = MakeSet(Rect(-30f, -20f, -25f, 20f));

			var cloud = LotFrontage.Build(lots, roads, DefaultSettings(), CancellationToken.None);

			Assert.AreEqual(0, cloud.Count);
		}

		[Test]
		public void NarrowFrontage_IsSkipped()
		{
			var lots = MakeSet(Rect(0f, 0f, 4f, 20f));
			var roads = MakeSet(Rect(-20f, -9f, 20f, -4f));

			var cloud = LotFrontage.Build(lots, roads, DefaultSettings(), CancellationToken.None);

			Assert.AreEqual(0, cloud.Count);
		}

		[Test]
		public void PlacementTooCloseToRoad_IsSkipped()
		{
			var lots = MakeSet(Rect(0f, 0f, 10f, 10f));
			var roads = MakeSet(Rect(-9f, -20f, -4f, 20f));
			var settings = DefaultSettings();
			settings.MinPlacementClearance = 8.5f;

			var cloud = LotFrontage.Build(lots, roads, settings, CancellationToken.None);

			Assert.AreEqual(0, cloud.Count);
		}

		[Test]
		public void PlacementTooFarFromRoad_IsSkipped()
		{
			var lots = MakeSet(Rect(0f, 0f, 10f, 10f));
			var roads = MakeSet(Rect(-9f, -20f, -4f, 20f));
			var settings = DefaultSettings();
			settings.MaxPlacementDistance = 7.5f;

			var cloud = LotFrontage.Build(lots, roads, settings, CancellationToken.None);

			Assert.AreEqual(0, cloud.Count);
		}

		[Test]
		public void SegmentedOutline_MergesCollinearEdges_AndKeepsFrontage()
		{
			var vertices = new List<float2>();
			for (int i = 0; i < 5; i++)
				vertices.Add(new float2(i * 2f, 0f));
			for (int i = 0; i < 5; i++)
				vertices.Add(new float2(10f, i * 2f));
			for (int i = 0; i < 5; i++)
				vertices.Add(new float2(10f - i * 2f, 10f));
			for (int i = 0; i < 5; i++)
				vertices.Add(new float2(0f, 10f - i * 2f));

			var lots = MakeSet(vertices.ToArray());
			var roads = MakeSet(Rect(-9f, -20f, -4f, 20f));

			var cloud = LotFrontage.Build(lots, roads, DefaultSettings(), CancellationToken.None);

			Assert.AreEqual(1, cloud.Count);
			Assert.AreEqual(4f, cloud[0].Position.x, 1e-3f);
			Assert.AreEqual(5f, cloud[0].Position.z, 1e-3f);
			Assert.AreEqual(10f, cloud.Attributes.Get<float>(CityAttributes.LotWidth, 0), 1e-3f);
		}

		[Test]
		public void SetbackJitter_StaysWithinBounds_AndVariesBySeed()
		{
			var lots = MakeSet(Rect(0f, 0f, 10f, 10f));
			var roads = MakeSet(Rect(-9f, -20f, -4f, 20f));
			var distinct = new HashSet<float>();

			for (int seed = 0; seed < 20; seed++)
			{
				var cloud = LotFrontage.Build(lots, roads, DefaultSettings(seed, 0.5f), CancellationToken.None);
				Assert.AreEqual(1, cloud.Count);
				float x = cloud[0].Position.x;
				Assert.GreaterOrEqual(x, 3.5f - 1e-3f);
				Assert.LessOrEqual(x, 4.5f + 1e-3f);
				distinct.Add(math.round(x * 1000f) / 1000f);
			}

			Assert.GreaterOrEqual(distinct.Count, 2);
		}
	}
}
