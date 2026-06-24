# ТДД: лишние прямоугольники в превью RegionToSpline (бокс-гизмо сплайнов)

Status: Выполнено

## Контекст

В сцене сравниваются три превью:

- жёлтое — исходный сплайн контура города;
- красное — регионы дорог (`Roads` из `BlocksToRoads`);
- голубое — те же регионы, прогнанные через `RegionToSpline`.

Симптом: в голубом превью поверх линий дорог появляются осевые прямоугольники, которых нет в красном. Самый большой охватывает весь город и торчит за его пределы. Воспринималось как «нода сквадрачивает сглаженные кривые региона».

## Диагностика

Линия выходного сплайна корректна. Замер round-trip (`SplineToRegion` → `RegionToSpline`) показал, что геометрия выходного сплайна совпадает с полигоном региона: перпендикулярное отклонение `0.0000` при любом `MaxSegmentLength`. Кноты идут с `TangentMode.Linear` (нулевые тангенсы) — это в точности ломаная региона. То есть кривизна не искажается, «квадратизации» линии нет.

Прямоугольники — это бокс-гизмо (AABB) каждого сплайна:

- `SplinesGizmoUtility.DrawGizmos` на каждый сплайн рисует `Gizmos.DrawWireCube` по его баундам; `RegionGizmoUtility` такого не делает. Поэтому боксы видны только в голубом (сплайн) превью и отсутствуют в красном (регион).
- Большой бокс — AABB периметрального дорожного сплайна. У «кляксы» города осевой бокс заметно больше самой фигуры, его углы уходят за контур.
- Дополнительно бокс смещён и раздут из-за двойной трансформации: `bounds` берутся уже в мировых координатах (`spline.GetBounds(transform.localToWorldMatrix)`), а рисуются под уже выставленным `Gizmos.matrix = transform.localToWorldMatrix`, то есть `localToWorld` применяется к ним второй раз.

Бокс-гизмо — отладочный рудимент: по назначению (`Docs/PROJECT_MAP.md`) `SplinesGizmoUtility` рисует только линии сплайнов.

## Файл

`Packages/PCG.Splines/Scripts/Utilities/SplinesGizmoUtility.cs`

## Изменение

Удалить из тела цикла `foreach` строки получения баундов и отрисовки бокса (строки 21–22):

```
				var bounds = spline.GetBounds(transform.localToWorldMatrix);
				Gizmos.DrawWireCube(bounds.center, bounds.size);
```

Метод после правки:

```
		public static void DrawGizmos(List<Spline> splines, Transform transform)
		{
			if (splines == null || splines.Count <= 0)
				return;

			Gizmos.matrix = transform.localToWorldMatrix;
			foreach (var spline in splines)
			{
				if(spline == null || spline.Count < 2)
					continue;

				Vector3[] positions;
				SplinesCache.GetCachedPositions(spline, 16, out positions);

#if UNITY_2023_1_OR_NEWER
                Gizmos.DrawLineStrip(positions, false);
#else
				for (int i = 1; i < positions.Length; ++i)
					Gizmos.DrawLine(positions[i-1], positions[i]);
#endif
			}
			Gizmos.matrix = Matrix4x4.identity;
		}
```

`transform` остаётся в сигнатуре — он по-прежнему задаёт `Gizmos.matrix`.

## Эффект

Бокс-гизмо исчезнет во всех превью, где используется `SplinesGizmoUtility` (все ноды сплайнов: `RegionToSpline`, `Resample`, `Offset`, `Smooth`, `Join`, `Closed`, `FindSplines`, `SplineFromPoints`, `RandomSpline`, `ChangeSplinePosition`, `SplineNode`, `SplineAroundPoints`). Это и есть цель — гизмо должно рисовать только линии.

## Шаги внедрения

- В `SplinesGizmoUtility.DrawGizmos` удалить две строки с `bounds` и `Gizmos.DrawWireCube`.
- В `WorldScene` сверить превью `RegionToSpline`: голубые линии повторяют красный регион, прямоугольников нет.

## После реализации

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить `Docs/PROJECT_MAP.md` (описание `SplinesGizmoUtility` менять не обязательно — там и так «отрисовка линий»).
