# ТДД: запись рёберных атрибутов после NormalizeWinding

Status: Выполнено

## Контекст

После гео-классификации (`260622-1547`) дороги стали лучше, но остаточные артефакты держатся: часть рёбер исходной границы становится дорогой, а часть резов теряет глубину (дорога не рисуется). Причина — рассинхрон рёберных атрибутов с геометрией в `PolygonEdgeClip.BuildPolygons`.

Подтверждено прогоном в редакторе (прямоугольник со сторонами, помеченными `side = индекс`, один рез):

```
30x40 horizontal cut, right[0]:
  e0 (30,0)->(0,0)  низ-граница: stored cutDepth=9  geom cutDepth=0   граница помечена как рез
  e2 (0,20)->(30,20) рез:        stored cutDepth=0  geom cutDepth=9   рез потерял глубину
```

`ResolveRing` пишет атрибуты в порядке кольца, которое отдал Clipper, а `PolygonClipper.NormalizeWinding` затем разворачивает кольцо (когда Clipper вернул его по часовой), не трогая `EdgeAttributes`. При развороте кольца из N вершин атрибуты противоположных рёбер меняются местами (в прямоугольнике — пара e0/e2). Если в паре один рез, другой граница — их глубины обмениваются. Гео-классификация при этом корректна — проблема в моменте записи относительно разворота.

## Фикс

Файл: `Packages/PCG.Polygons/Scripts/Geometry/PolygonEdgeClip.cs`.

Сначала строим кольца (только координаты), нормализуем винтинг, и **после** этого присваиваем рёберные атрибуты по финальной геометрии. `GeometricSource` матчит по середине ребра — порядок и разворот кольца на результат не влияют.

`BuildPolygons`:

```
		private static void BuildPolygons(PolyTree64 tree, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter, List<Polygon2D> result)
		{
			for (int i = 0; i < tree.Count; i++)
			{
				var node = tree[i];
				var polygon = new Polygon2D();
				polygon.Outer = ToRing(node.Polygon);
				for (int h = 0; h < node.Count; h++)
				{
					polygon.Holes.Add(ToRing(node[h].Polygon));
				}

				PolygonClipper.NormalizeWinding(polygon);
				AssignEdges(polygon, table, newEdgeWriter);
				result.Add(polygon);
			}
		}
```

Вместо `ResolveRing` — `ToRing` (только геометрия) и `AssignEdges`/`AssignRing` (атрибуты по финальным рёбрам, в плоском порядке: внешний контур, затем дырки):

```
		private static float2[] ToRing(Path64 path)
		{
			int n = path.Count;
			var ring = new float2[n];
			for (int i = 0; i < n; i++)
			{
				ring[i] = new float2((float)(path[i].X / PolygonClipper.Scale), (float)(path[i].Y / PolygonClipper.Scale));
			}

			return ring;
		}

		private static void AssignEdges(Polygon2D polygon, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter)
		{
			AssignRing(polygon.Outer, table, newEdgeWriter, polygon.EdgeAttributes);
			for (int h = 0; h < polygon.Holes.Count; h++)
			{
				AssignRing(polygon.Holes[h], table, newEdgeWriter, polygon.EdgeAttributes);
			}
		}

		private static void AssignRing(float2[] ring, List<EdgeSource> table, Action<PcgAttributeSet, int> newEdgeWriter, PcgAttributeSet edgeAttributes)
		{
			int n = ring.Length;
			for (int i = 0; i < n; i++)
			{
				var a = ring[i];
				var b = ring[(i + 1) % n];
				int sourceId = GeometricSource(a, b, table);
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
		}
```

Метод `ResolveRing` удалить. `GeometricSource` и `OnSegment` остаются как есть.

## Эффект

Рёберные атрибуты присваиваются по финальной (уже нормализованной) геометрии, разворот колец на них не влияет. Рёбра границы остаются `cutDepth = 0`, рёбра-резы держат свою глубину на всей длине — остаточные артефакты уходят.

## Связь с другими ТДД

- Завершает `260622-1547-TDD-edge_classification_geometric`: способ классификации (`GeometricSource`) оттуда верный, но запись атрибутов должна идти после `NormalizeWinding`. Структура `ResolveRing` из того ТДД заменяется на `ToRing` + `AssignEdges`.

## Шаги внедрения

- `PolygonEdgeClip.cs`: в `BuildPolygons` строить кольца через `ToRing`, вызвать `NormalizeWinding`, затем `AssignEdges`; добавить `ToRing`, `AssignEdges`, `AssignRing`; удалить `ResolveRing`.

## После реализации

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить `Docs/PROJECT_MAP.md`.
