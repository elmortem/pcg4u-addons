using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Instances;

namespace PCG.Polygons.City
{
	public sealed class CombineInstancesNodeExecutor : PcgAsyncNodeExecutor<CombineInstancesNode>
	{
		public PcgOutput<List<InstanceData>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<InstanceData>();
			var inputs = GetInputValues(nameof(Data.Instances), Data.Instances);
			if (inputs == null)
				return UniTask.CompletedTask;

			foreach (IReadOnlyList<InstanceData> instances in inputs)
			{
				if (instances == null)
					continue;

				for (int i = 0; i < instances.Count; i++)
					Results.Value.Add(instances[i]);
			}

			return UniTask.CompletedTask;
		}
	}
}
