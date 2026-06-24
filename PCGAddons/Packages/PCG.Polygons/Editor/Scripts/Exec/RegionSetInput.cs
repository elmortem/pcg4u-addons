using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Utilities;

namespace PCG.Polygons
{
	public static class RegionSetInput
	{
		public static async UniTask<RegionSet> ReadCombinedAsync(PcgNodeExecutor executor, string fieldName, CancellationToken ct)
		{
			var sets = executor.GetInputValues<RegionSet>(fieldName);
			if (sets == null || sets.Length <= 0)
				return null;

			var valid = new List<RegionSet>(sets.Length);
			for (int i = 0; i < sets.Length; i++)
			{
				if (sets[i] != null)
					valid.Add(sets[i]);
			}

			if (valid.Count <= 0)
				return null;

			if (valid.Count == 1)
				return valid[0];

			await UniTask.SwitchToThreadPool();

			var result = new RegionSet();
			result.PlaneY = valid[0].PlaneY;
			for (int i = 0; i < valid.Count; i++)
			{
				if (ct.IsCancellationRequested)
					break;

				result.Append(valid[i]);
			}

			await UniTaskEditor.SwitchToEditorThread();
			return result;
		}
	}
}
