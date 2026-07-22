using Unity.Mathematics;

namespace PCG.Sweep
{
	internal sealed class SweepJunctionFootprintGap
	{
		public float2[] Plan;
		public float[] T;
		public float2 ReferenceStart;
		public float2 ReferenceEnd;

		internal float2 Transform(int index, float2 start, float2 end)
		{
			float t = T[index];
			if (index == 0)
				return start;
			if (index == T.Length - 1)
				return end;

			float2 referenceChord = ReferenceEnd - ReferenceStart;
			float referenceLength = math.length(referenceChord);
			float2 actualChord = end - start;
			float actualLength = math.length(actualChord);
			if (referenceLength < 1e-6f || actualLength < 1e-6f)
				return math.lerp(start, end, t);

			float2 referenceForward = referenceChord / referenceLength;
			float2 referenceSide = new float2(-referenceForward.y, referenceForward.x);
			float2 actualForward = actualChord / actualLength;
			float2 actualSide = new float2(-actualForward.y, actualForward.x);
			float2 residual = Plan[index] - math.lerp(ReferenceStart, ReferenceEnd, t);
			float scale = math.clamp(actualLength / referenceLength, 0.25f, 4f);
			float along = math.dot(residual, referenceForward) * scale;
			float side = math.dot(residual, referenceSide) * scale;
			return math.lerp(start, end, t) + actualForward * along + actualSide * side;
		}
	}
}
