using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Splines;
using PCG.Utilities;
using UnityEngine.U2D;
using Spline = UnityEngine.Splines.Spline;

namespace PCG.SpriteShapes
{
	public class SpriteShapeInstanceNodeExecutor : PcgAsyncNodeExecutor<SpriteShapeInstanceNode>
	{
		public PcgOutput<List<SpriteShapeInstanceData>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<SpriteShapeInstanceData>();

			if (!Data.Enabled)
				return;

			var splinesPort = GetInputPort(nameof(Data.Splines));
			var splinesList = splinesPort.GetInputValues();
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var spriteShape = GetInputValue(nameof(Data.SpriteShape), Data.SpriteShape);
			if (spriteShape == null)
				return;

			var spriteShapeName = GetInputValue(nameof(Data.Name), Data.Name);
			var height = GetInputValue(nameof(Data.Height), Data.Height);

			using (var scope = OperationScope.Start(this))
			{
				foreach (PcgSplineSet splines in splinesList)
				{
					if (splines == null || splines.Count <= 0)
						continue;

					foreach (var spline in splines)
					{
						var instance = new SpriteShapeInstanceData
							{ Name = spriteShapeName, Spline = spline, SpriteShape = spriteShape, Height = height };
						Results.Value.Add(instance);

						await scope.Step(ct: ct);
					}
				}
			}
		}
	}
}
