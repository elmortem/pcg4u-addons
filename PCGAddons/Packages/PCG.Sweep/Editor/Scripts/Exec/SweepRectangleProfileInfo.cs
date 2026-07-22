using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepRectangleProfileInfo
	{
		internal float Height;
		internal float Width;
		internal float2[] BottomPoints;
		internal float[] BottomUs;
		internal float2[] TopPoints;
		internal float[] TopUs;

		internal static bool TryCreate(SweepSnapshot snapshot, out SweepRectangleProfileInfo result, out string failure)
		{
			result = null;
			failure = null;
			if (snapshot == null || snapshot.ProfilePoints == null || snapshot.ProfileUs == null || snapshot.ProfileSegments == null)
			{
				failure = "RectangleProfileMissing";
				return false;
			}
			if (!snapshot.ProfileClosed || snapshot.ProfilePoints.Length != 8 || snapshot.ProfileUs.Length != 8 || snapshot.ProfileSegments.Length != 8)
			{
				failure = "RectangleBuiltInProfileRequired";
				return false;
			}
			for (int index = 0; index < 8; index++)
			{
				if (snapshot.ProfileSegments[index] != index || !math.all(math.isfinite(snapshot.ProfilePoints[index])) || !math.isfinite(snapshot.ProfileUs[index]))
				{
					failure = "RectangleBuiltInProfileRequired";
					return false;
				}
			}

			float halfWidth = snapshot.ProfilePoints[0].x;
			float height = snapshot.ProfilePoints[3].y;
			float scale = math.max(1f, math.max(math.abs(halfWidth), math.abs(height)));
			float tolerance = scale * 1e-5f;
			if (halfWidth <= tolerance || height <= tolerance)
			{
				failure = "RectangleDimensionsInvalid";
				return false;
			}

			var expectedPoints = new[]
			{
				new float2(halfWidth, 0f),
				new float2(-halfWidth, 0f),
				new float2(-halfWidth, 0f),
				new float2(-halfWidth, height),
				new float2(-halfWidth, height),
				new float2(halfWidth, height),
				new float2(halfWidth, height),
				new float2(halfWidth, 0f)
			};
			for (int index = 0; index < expectedPoints.Length; index++)
			{
				if (math.distance(snapshot.ProfilePoints[index], expectedPoints[index]) > tolerance)
				{
					failure = "RectangleBuiltInProfileRequired";
					return false;
				}
			}

			float perimeter = 4f * halfWidth + 2f * height;
			var expectedUs = new[]
			{
				0f,
				2f * halfWidth / perimeter,
				2f * halfWidth / perimeter,
				(2f * halfWidth + height) / perimeter,
				(2f * halfWidth + height) / perimeter,
				(4f * halfWidth + height) / perimeter,
				(4f * halfWidth + height) / perimeter,
				1f
			};
			for (int index = 0; index < expectedUs.Length; index++)
			{
				if (math.abs(snapshot.ProfileUs[index] - expectedUs[index]) > 1e-5f)
				{
					failure = "RectangleBuiltInProfileRequired";
					return false;
				}
			}

			if (!ValidateLut(snapshot.WidthLut, true) || !ValidateLut(snapshot.HeightLut, true))
			{
				failure = "RectangleScaleLutInvalid";
				return false;
			}
			if (!ValidateLut(snapshot.TwistLut, false))
			{
				failure = "RectangleTwistLutInvalid";
				return false;
			}
			for (int index = 0; index < snapshot.TwistLut.Length; index++)
			{
				if (math.abs(snapshot.TwistLut[index]) > 1e-4f)
				{
					failure = "RectangleZeroTwistRequired";
					return false;
				}
			}

			result = new SweepRectangleProfileInfo
			{
				Height = height,
				Width = halfWidth * 2f,
				BottomPoints = new[] { expectedPoints[0], expectedPoints[1] },
				BottomUs = new[] { expectedUs[0], expectedUs[1] },
				TopPoints = new[] { expectedPoints[5], expectedPoints[4] },
				TopUs = new[] { expectedUs[5], expectedUs[4] }
			};
			return true;
		}

		private static bool ValidateLut(float[] values, bool positive)
		{
			if (values == null || values.Length == 0)
				return false;
			for (int index = 0; index < values.Length; index++)
			{
				if (!math.isfinite(values[index]))
					return false;
				if (positive && values[index] <= 1e-5f)
					return false;
			}
			return true;
		}
	}
}
