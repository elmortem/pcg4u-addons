# TDD: Режимы 2D/3D и UseScale для PointsNearSplines

Status: Выполнено

## Контекст

Нода `PointsNearSplines` делит входные точки на два выхода: точки рядом со сплайнами (`NearPoints`) и остальные (`Results`). Близость определяется сэмплированием сплайнов в кэш точек и сравнением квадрата расстояния с порогом `Distance`.

Нужно добавить:

- Режим работы `Mode` со значениями 3D (текущее поведение) и 2D (близость считается только по XZ, Y игнорируется).
- Галку `UseScale` по образцу `PointsNearPointsOctreeNode`: при включении радиус домножается на `point.Scale`.

## Затрагиваемые файлы

- `Packages/PCG.Splines/Scripts/SelectPoints/PointsNearSplinesMode.cs` — новый файл с enum.
- `Packages/PCG.Splines/Scripts/SelectPoints/PointsNearSplinesNode.cs` — два новых поля.
- `Packages/PCG.Splines/Editor/Scripts/Exec/PointsNearSplinesNodeExecutor.cs` — логика расчёта расстояния в `CheckNearSpline`.

## Новый enum

Создать файл `Packages/PCG.Splines/Scripts/SelectPoints/PointsNearSplinesMode.cs`:

```
namespace PCG.SelectPoints
{
	public enum PointsNearSplinesMode
	{
		ThreeD,
		TwoD
	}
}
```

## Изменения в ноде

В `PointsNearSplinesNode` добавить два инспекторных поля (по образцу `Spacing` и `UseScale` в соседних нодах — обычные поля, без `[Input]`):

```
public PointsNearSplinesMode Mode = PointsNearSplinesMode.ThreeD;
public bool UseScale;
```

Итоговый вид класса:

```
using System.Collections.Generic;
using UnityEngine.Splines;
using PCG.Points;
using PCG.GraphModel;

namespace PCG.SelectPoints
{
	public class PointsNearSplinesNode : PcgPreviewNode
	{
		[Output] public List<PointData> Results => default;
		[Output] public List<PointData> NearPoints => default;

		[Input] public List<PointData> Points = new();
		[Input] public List<Spline> Splines;
		[Input] public float Distance = 1f;

		public PointsNearSplinesMode Mode = PointsNearSplinesMode.ThreeD;
		public bool UseScale;
	}
}
```

## Изменения в executor

Меняется только метод `CheckNearSpline`. Сэмплирование сплайнов в `_pointsCache` остаётся без изменений — плотность сэмплов считается по базовому `distance` (общий кэш для всех точек, на per-point масштаб не завязан).

Меняется блок вычисления порога и сравнения:

- Эффективный радиус для точки: при `Data.UseScale` базовый `distance` домножается на `point.Scale`.
- В режиме `TwoD` у вектора разницы обнуляется компонента Y перед расчётом `lengthsq`.

Новый вид метода `CheckNearSpline` (блок построения кэша не трогаем):

```
private bool CheckNearSpline(PointData point, List<Spline>[] splinesList, float distance)
{
	if (_pointsCache.Count <= 0)
	{
		foreach (var splines in splinesList)
		{
			if (splines == null || splines.Count <= 0)
				continue;

			foreach (var spline in splines)
			{
				var splineLen = spline.GetLength();
				var count = Mathf.RoundToInt(splineLen / distance * 1.5f) + 2;
				var step = 1f / count;

				for (int i = 0; i <= count; i++)
				{
					_pointsCache.Add(spline.EvaluatePosition(i * step));
				}
			}
		}
	}

	var effectiveDistance = distance;
	if (Data.UseScale)
		effectiveDistance *= point.Scale;

	var sqrDist = effectiveDistance * effectiveDistance;
	var pointPosition = (float3)point.Position;

	foreach (var pointCache in _pointsCache)
	{
		var delta = pointCache - pointPosition;
		if (Data.Mode == PointsNearSplinesMode.TwoD)
			delta.y = 0f;

		if (math.lengthsq(delta) < sqrDist)
		{
			return true;
		}
	}

	return false;
}
```

## Поведение

- `Mode = ThreeD`, `UseScale = false` — полностью совпадает с текущим поведением (обратная совместимость, дефолтные значения).
- `Mode = TwoD` — близость по горизонтали: Y точки и Y сэмпла сплайна не влияют на результат.
- `UseScale = true` — порог близости для каждой точки равен `Distance * point.Scale`; работает в обоих режимах.

---

## После выполнения

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновлять документацию проекта (`Docs/PROJECT_MAP.md`, `Packages/PCG.Splines/Documentation~/.../Points-Near-Splines-Node.md`) под эти изменения.
