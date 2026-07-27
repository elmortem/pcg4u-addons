using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Instances;
using PCG.Points;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;

namespace PCG.BRG
{
	public class GameObjectToBrgNodeExecutor : PcgAsyncPreviewNodeExecutor<GameObjectToBrgNode>, INodeInfo
	{
		public PcgOutput<List<BrgInstanceData>> Results;

		private readonly Dictionary<GameObject, BrgInstanceData> _tmpResults = new();

		public override bool IsEmpty => Results.Value == null;
		public bool HasNodeInfo => !IsEmpty;

		public string NodeInfo
		{
			get
			{
				var prefabsCount = 0;
				var pointsCount = 0;
				if (_tmpResults.Count > 0)
				{
					prefabsCount = _tmpResults.Count;
					pointsCount = _tmpResults.Values.Sum(p => p.Points.Count);
				}
				else if (Results.Value != null)
				{
					prefabsCount = Results.Value.Count;
					pointsCount = Results.Value.Sum(p => p.Points.Count);
				}
				return $"Objects: {pointsCount} / {prefabsCount}";
			}
		}

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<BrgInstanceData>();
			_tmpResults.Clear();

			if (!Data.Enabled)
				return;

			var instancesPort = GetInputPort(nameof(Data.Instances));
			var instancesList = instancesPort.GetInputValues();
			if (instancesList == null || instancesList.Length <= 0)
				return;

			using (var scope = OperationScope.Start(this))
			{
				foreach (List<GameObjectInstanceData> instances in instancesList)
				{
					if (instances == null)
						continue;

					foreach (var instance in instances)
					{
						if (!_tmpResults.TryGetValue(instance.Prefab, out var brgInstance))
						{
							brgInstance = new BrgInstanceData { Prefab = instance.Prefab };
							_tmpResults[instance.Prefab] = brgInstance;
						}

						brgInstance.Points.Add(instance.Point);
						var scale3Column = brgInstance.Points.Attributes.EnsureColumn<float3>(PcgPointAttributes.Scale3);
						scale3Column.Values[brgInstance.Points.Count - 1] = instance.Scale3;

						await scope.Step(ct: ct);
					}
				}

				Results.Value.AddRange(_tmpResults.Values);
				_tmpResults.Clear();
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			GizmosUtility.DrawPoints(Results.Value.SelectMany(p => p.Points.Points).ToList(), gizmosOptions, transform);
		}
	}
}
