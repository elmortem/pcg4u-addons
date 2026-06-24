# ТДД: геометрическая классификация рёбер в PolygonEdgeClip

Status: Выполнено

## Контекст

Настоящая причина двух артефактов дорог — гребёнки вдоль исходной границы и «кармана» (широкая дорога упирается в узкую) — в `PolygonEdgeClip`. Классификация выходных рёбер опирается на Z-id Clipper2 (`OnZ` пишет в точку пересечения `max` из Z четырёх концов). Для рёбер, рассечённых линией реза, и на углах Z-id'ы сталкиваются, и `ClassifyEdge` сопоставляет выходное ребро неправильному ребру субъекта.

Подтверждено прогоном в редакторе (рез квадрата 100×100 линией `x=50`, рёбра субъекта помечены `orig = индекс`):

```
left  e0 bottom (50,0)->(0,0):    cutDepth=1  (должно 0)   граница помечена как рез
left  e2 top    (0,100)->(50,100): orig=0     (должно 2)   унаследован атрибут чужого ребра
right e0 bottom (100,0)->(50,0):  orig=2      (должно 0)
right e2 top    (50,100)->(100,100): cutDepth=1 (должно 0)
```

Рёбра, не задетые резом (`x=0` слева, `x=100` справа), классифицируются верно. Сбой — на рёбрах, которые рез рассекает. Матч по середине ребра против отрезков субъекта даёт корректный источник для всех рёбер.

## Следствие в дорогах

- Рёбра границы, рассечённые резом, частью становятся «новыми» → получают `newEdgeWriter` → `cutDepth` → дороги вдоль исходной кривой (гребёнка), и держатся даже после правок `cutDepth`.
- Рёбра дороги частью наследуют `cutDepth` чужого (более глубокого) реза → ширина вдоль одной улицы скачет → карман на стыке широкой и узкой.

## Фикс: матч по геометрии

Файл: `Packages/PCG.Polygons/Scripts/Geometry/PolygonEdgeClip.cs`.

Выходное ребро наследует атрибуты того ребра субъекта, на отрезке которого лежит его середина; если такого ребра нет — это новое ребро. `table` уже хранит `A`/`B`/`Polygon`/`LocalEdge` каждого ребра субъекта, индекс по `table` сохраняется как и раньше.

`ResolveRing` — классификация через `GeometricSource`:

```
			for (int i = 0; i < n; i++)
			{
				int next = (i + 1) % n;
				int sourceId = GeometricSource(ring[i], ring[next], table);
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

Новые хелперы:

```
		private static int GeometricSource(float2 a, float2 b, List<EdgeSource> table)
		{
			var m = (a + b) * 0.5f;
			for (int i = 0; i < table.Count; i++)
			{
				if (OnSegment(m, table[i].A, table[i].B))
					return i + 1;
			}

			return 0;
		}

		private static bool OnSegment(float2 m, float2 c, float2 d)
		{
			var dir = d - c;
			float len = math.length(dir);
			if (len < 1e-4f)
				return false;

			dir /= len;
			float cross = dir.x * (m.y - c.y) - dir.y * (m.x - c.x);
			if (math.abs(cross) > 0.01f)
				return false;

			float t = math.dot(m - c, dir);
			return t >= -0.01f && t <= len + 0.01f;
		}
```

Удалить ставший ненужным Z-механизм классификации:

- методы `OnZ`, `ClassifyEdge`, `TryCandidate`, `IsCollinearOverlap`, `Cross`;
- строку `clipper.ZCallback = OnZ;` в `Execute`;
- присваивание `point.Z = id;` в `AppendRing` и `point.Z = 0;` в `ClipRing` (в `AppendRing` сам `table.Add(...)` и индекс остаются — они нужны для `GeometricSource`).

`USINGZ` и `Clipper2ZLib` не трогаем.

## Эффект

- Рёбра исходной границы наследуют свои атрибуты, `cutDepth` не получают → `AssignRoadClassByDepth` их пропускает → гребёнки нет.
- Рассечённые рёбра дороги наследуют `cutDepth` своего реза → ширина вдоль улицы постоянна → кармана нет.

## Связь с другими ТДД

- Заменяет правку `ResolveRing` из `260622-1524-TDD-road_marking_fix` (Фикс 1) — её применять не нужно. Фикс 2 оттуда (гейт `MaxDepth` в `AssignRoadClassByDepth`) остаётся в силе.
- `cutDepth = depth + 1` и пропуск `d <= 0` из `260622-1409-TDD-city_nodes_fixes` остаются нужны: отличают границу (`0`) от реза глубины 0 (`1`).

## Шаги внедрения

- `PolygonEdgeClip.cs`: заменить классификацию в `ResolveRing` на `GeometricSource`; добавить `GeometricSource` и `OnSegment`; удалить `OnZ`, `ClassifyEdge`, `TryCandidate`, `IsCollinearOverlap`, `Cross`, `clipper.ZCallback`, присваивания `point.Z`.

## После реализации

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить `Docs/PROJECT_MAP.md` — там Z-callback описан как механизм проброса рёберных атрибутов, после фикса описание устарело.
