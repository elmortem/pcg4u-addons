Status: Выполнено

# TDD: Точки на сплайне по дистанции

## Задача

Две связанные правки в пакете `PCG.Splines`:

- Починить режим `SurfaceRegular` у `SplinesSurfaceNode`: сейчас точки раскладываются по параметру `t` (`i/count`), из-за чего на кривых участках реальное расстояние между ними гуляет. Нужна раскладка по длине дуги.
- Добавить новую ноду `SplinePointsByDistanceNode`, которая ставит точки вдоль сплайна с заданным шагом `Distance` (в метрах).

Poisson в этой задаче не участвует — он выносится в отдельный фильтр отдельным ТДД.

## Затрагиваемые файлы

- `Packages/PCG.Splines/Scripts/Surfaces/SplinePoints.cs` — правка `GetSurfaceRegularPoints`, новый метод `GetPointsByDistance`.
- `Packages/PCG.Splines/Scripts/CreatePoints/SplinePointsByDistanceNode.cs` — новый файл, нода.
- `Packages/PCG.Splines/Editor/Scripts/Exec/SplinePointsByDistanceNodeExecutor.cs` — новый файл, исполнитель.
- `Packages/PCG.Splines/Documentation~/PCG.Splines/CreatePoints/Spline-Points-By-Distance-Node.md` — новый файл, документация ноды.
- `Docs/PROJECT_MAP.md` — добавить новую ноду в карту.

## Используемое API Unity.Splines

- `spline.GetLength()` — полная длина сплайна.
- `spline.ConvertIndexUnit(value, PathIndexUnit.Distance, PathIndexUnit.Normalized)` — перевод дистанции вдоль дуги в нормализованный `t`.
- `spline.Evaluate(t, out position, out tangent, out upVector)` — позиция, касательная, вектор «вверх» в точке `t`.
- `spline.Closed` — замкнут ли сплайн.

## Правка SurfaceRegular

Файл `Packages/PCG.Splines/Scripts/Surfaces/SplinePoints.cs`, метод `GetSurfaceRegularPoints`.

Заменить тело на раскладку по длине дуги. Открытая конвенция сохраняется: `count` точек на дистанциях `length * i / count` для `i` от `0` до `count - 1`, конец дуги не достигается.

```csharp
private static async UniTask GetSurfaceRegularPoints(OperationScope scope, List<PointData> results, Spline spline, int count, Vector3 offset, CancellationToken ct)
{
	var length = spline.GetLength();
	if (length <= 0f)
		return;

	for (int i = 0; i < count; ++i)
	{
		var distance = length * i / count;
		var t = spline.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
		spline.Evaluate(t, out var point, out var tangent, out var upVector);
		results.Add(new PointData
		{
			Position = offset + (Vector3)point,
			Normal = upVector,
			Scale = 1f,
			Angle = Quaternion.LookRotation(tangent, upVector).eulerAngles.y
		});

		await scope.Step(ct: ct);
	}
}
```

## Новый метод GetPointsByDistance

Файл `Packages/PCG.Splines/Scripts/Surfaces/SplinePoints.cs`, добавить в класс `SplinePoints`.

Поведение шага задаётся флагом `distribute`:

- `distribute = true` — шаг подгоняется так, чтобы точки сели ровно. На незамкнутом сплайне первая и последняя точки попадают на концы дуги. На замкнутом точки распределяются по кольцу без дубля на шве.
- `distribute = false` — жёсткий шаг `distance` от начала, хвост обрезается. На замкнутом сплайне точка, совпадающая со швом, не ставится.

```csharp
public static async UniTask GetPointsByDistance(OperationScope scope, List<PointData> results, Spline spline, float distance, bool distribute, CancellationToken ct = default)
{
	if (spline == null)
	{
		Debug.LogWarning("Spline is not assigned.");
		return;
	}

	if (distance <= 0f)
		return;

	var length = spline.GetLength();
	if (length <= 0f)
		return;

	var intervals = math.max(1, Mathf.RoundToInt(length / distance));

	int count;
	float step;

	if (distribute)
	{
		step = length / intervals;
		count = spline.Closed ? intervals : intervals + 1;
	}
	else
	{
		step = distance;
		count = Mathf.FloorToInt(length / distance) + 1;
		if (spline.Closed && Mathf.Approximately((count - 1) * distance, length))
			count -= 1;
	}

	count = math.min(count, PCG.MaxListPoints);

	for (int i = 0; i < count; i++)
	{
		var pointDistance = step * i;
		var t = spline.ConvertIndexUnit(pointDistance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
		spline.Evaluate(t, out var point, out var tangent, out var upVector);
		results.Add(new PointData
		{
			Position = (Vector3)point,
			Normal = upVector,
			Scale = 1f,
			Angle = Quaternion.LookRotation(tangent, upVector).eulerAngles.y
		});

		await scope.Step(ct: ct);
	}
}
```

## Нода SplinePointsByDistanceNode

Файл `Packages/PCG.Splines/Scripts/CreatePoints/SplinePointsByDistanceNode.cs`.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.GraphModel;
using PCG.Points;

namespace PCG.CreatePoints
{
	[Serializable]
	public class SplinePointsByDistanceNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;

		[Input] public List<Spline> Splines;
		[Input] public float Distance = 1f;
		public bool Distribute = true;
	}
}
```

## Исполнитель SplinePointsByDistanceNodeExecutor

Файл `Packages/PCG.Splines/Editor/Scripts/Exec/SplinePointsByDistanceNodeExecutor.cs`.

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Splines.Surfaces;
using UnityEngine;
using UnityEngine.Splines;
using PCG.Points;
using PCG.Utilities;
using PCG.Exec;
using PCG.GraphModel;

namespace PCG.CreatePoints
{
	public class SplinePointsByDistanceNodeExecutor : PcgAsyncPreviewNodeExecutor<SplinePointsByDistanceNode>, IPointsCount
	{
		public PcgOutput<List<PointData>> Results;

		public override bool IsEmpty => Results.Value == null;
		public int PointsCount => Results.Value?.Count ?? 0;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<PointData>();

			var splinesPort = GetInputPort(nameof(Data.Splines));
			var splinesList = splinesPort.GetInputValues();
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var distance = GetInputValue(nameof(Data.Distance), Data.Distance);
			if (distance <= 0f)
				return;

			using (var scope = OperationScope.Start(this))
			{
				foreach (List<Spline> splines in splinesList)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline == null)
							continue;

						await SplinePoints.GetPointsByDistance(scope, Results.Value, spline, distance, Data.Distribute, ct);
					}
				}
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			GizmosUtility.DrawPoints(Results.Value, gizmosOptions, transform);
		}
	}
}
```

## Документация ноды

Файл `Packages/PCG.Splines/Documentation~/PCG.Splines/CreatePoints/Spline-Points-By-Distance-Node.md`.

```markdown
# SplinePointsByDistanceNode

Generates points along input splines spaced by a fixed distance along the arc length.

## Inputs

### Splines

Input splines to generate points from.

### Distance

Target spacing between points, in meters along the spline.

## Variables

### Distribute

When enabled, the step is adjusted so that points fit the spline exactly: on an open spline the first and last points land on the ends, on a closed spline points are distributed around the loop without a duplicate at the seam. When disabled, a fixed step of `Distance` is used from the start and the remainder is cut.

## Outputs

### Results

The list of generated points.
```

## Порядок реализации

- Поправить `GetSurfaceRegularPoints` в `SplinePoints.cs`.
- Добавить `GetPointsByDistance` в `SplinePoints.cs`.
- Создать `SplinePointsByDistanceNode.cs`.
- Создать `SplinePointsByDistanceNodeExecutor.cs`.
- Создать документацию ноды.
- Добавить ноду в `Docs/PROJECT_MAP.md`.

---

После выполнения:

- Поменяй статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта под внесённые изменения.
