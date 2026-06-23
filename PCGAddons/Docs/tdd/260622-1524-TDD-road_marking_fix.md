# ТДД: фикс маркировки дорог (ResolveRing, гейт глубины)

Status: Выполнено

## Контекст

Догоняющий фикс к `260622-1409-TDD-city_nodes_fixes`. После него гребёнка осталась: дороги строятся вдоль всей исходной границы, `AssignRoadClassByDepth.MaxDepth` не ограничивает глубину дорог. Причина гребёнки — в `PolygonEdgeClip.ResolveRing`, а не в `cutDepth`.

## Корень гребёнки

При первом резе корневой регион из `SplineToRegion` не имеет рёберных атрибутов (`HasEdgeData() == false`). В `ResolveRing` унаследованное ребро границы совпадает с ребром субъекта (`sourceId > 0`), но из-за `!src.Polygon.HasEdgeData()` проваливается в ветку нового ребра и получает `newEdgeWriter` → `cutDepth = depth + 1 = 1`. Так все рёбра исходной границы помечаются `cutDepth = 1`, проходят проверку `d > 0` в `AssignRoadClassByDepth` и становятся дорогами. Сдвиг `cutDepth` на +1 тут не помогает — граница реально помечена как рез.

## Фикс 1: ResolveRing

Файл: `Packages/PCG.Polygons/Scripts/Geometry/PolygonEdgeClip.cs`, метод `ResolveRing` (строки 177-193).

Унаследованное ребро (`sourceId > 0`) никогда не вызывает `newEdgeWriter`: есть рёберные данные у источника — копируем строку, нет — добавляем пустую. `newEdgeWriter` только для действительно новых рёбер (`sourceId == 0`).

```
			for (int i = 0; i < n; i++)
			{
				int next = (i + 1) % n;
				int sourceId = ClassifyEdge(ring[i], ring[next], path[i].Z, path[next].Z, table);
				if (sourceId > 0)
				{
					var src = table[sourceId - 1];
					if (src.Polygon.HasEdgeData())
						edgeAttributes.AppendRow(src.Polygon.EdgeAttributes, src.LocalEdge);
					else
						edgeAttributes.AddRow();

					continue;
				}

				int row = edgeAttributes.AddRow();
				newEdgeWriter?.Invoke(edgeAttributes, row);
			}
```

После фикса рёбра исходной границы остаются с `cutDepth = 0` (дефолт), `AssignRoadClassByDepth` их пропускает, `BlocksToRoads` полос вдоль границы не строит.

## Фикс 2: гейт глубины дорог

Файл: `Packages/PCG.Polygons/Editor/Scripts/Exec/AssignRoadClassByDepthNodeExecutor.cs`, цикл по рёбрам.

`MaxDepth` ноды теперь ограничивает глубину дорог: рез становится дорогой только если глубина его рекурсии (`d - 1`) меньше `MaxDepth`. `Subdivide` режет до своей `MaxDepth` (мелкие кварталы под дома), дороги рисуются только до `MaxDepth` этой ноды.

```
					for (int e = 0; e < polygon.EdgeCount; e++)
					{
						int d = polygon.GetEdge<int>(CityAttributes.CutDepth, e);
						if (d <= 0)
							continue;

						if (d - 1 >= maxDepth)
							continue;

						float k = maxDepth > 0 ? (float)(d - 1) / maxDepth : 0f;
						float width = Data.WidthByDepth.Evaluate(k) * maxWidth;
						polygon.SetEdge(CityAttributes.Width, e, width);
					}
```

При `Subdivide.MaxDepth = 6` и `AssignRoadClassByDepth.MaxDepth = 4` дороги идут по резам глубин 0..3; более глубокие резы (4, 5) дорогами не становятся — внутренние под-кварталы домами не разделяются дорогой. При равных `MaxDepth` поведение прежнее (дороги по всем резам).

## Шаги внедрения

- `PolygonEdgeClip.cs`: в `ResolveRing` унаследованные рёбра всегда `continue` (копия строки источника либо пустая строка); `newEdgeWriter` только для `sourceId == 0`.
- `AssignRoadClassByDepthNodeExecutor.cs`: добавить гейт `if (d - 1 >= maxDepth) continue;` перед расчётом ширины.

## После реализации

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить `Docs/PROJECT_MAP.md`.
