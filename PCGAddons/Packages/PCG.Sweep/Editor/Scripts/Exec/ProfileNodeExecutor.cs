using PCG.Exec;
using UnityEngine;

namespace PCG.Sweep
{
	public class ProfileNodeExecutor : PcgSyncNodeExecutor<ProfileNode>
	{
		public PcgOutput<SweepProfile> Profile;

		public override bool IsEmpty => Profile.Value == null;

		protected override void DoCompute()
		{
			var width = GetInputValue(nameof(Data.Width), Data.Width);
			var height = GetInputValue(nameof(Data.Height), Data.Height);
			Profile.Value = SweepProfileBuilder.Build(Data.Shape, width, height, Data.Sides, Data.CustomPoints, Data.CustomClosed, Warn);
		}

		private void Warn(string message)
		{
			Debug.LogWarning($"[Profile] {message}");
		}
	}
}
