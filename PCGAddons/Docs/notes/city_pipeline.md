# Алгоритм генерации города (PCG.Polygons)

Практическая сборка графа из city-нод: от замкнутого сплайна-контура до домов на рельефе.

## Граф целиком

```
Spline (замкнутый контур города)
  → SplineToRegion
  → [PolygonBoolean(Difference) ← препятствия]        (опц.)
  → SubdivideRegion                                    (кварталы)
  → AssignRoadClassByDepth                             (ширина рёбер по глубине)
  ├─ BlocksToRoads                                     (выход `Roads`: RegionSet)
  │     → RegionToTerrain                              (TODO — ноды ещё нет)
  │     → RegionToSpline → MeshAlongSpline             (TODO — ноды ещё нет)
  └─ InsetRegion(Delta<0)                              (отступ кварталов от дорог)
        → LotsFromBlock                                (нарезка на лоты)
        → RegionToPoints (Roads ← из BlocksToRoads)    (точка размещения дома)
        → weighted instancer (дом)
        → PointToTerrain                               (посадить на рельеф)
```

Всё считается в плоскости XZ на одной высоте `RegionSet.PlaneY`; проекция на рельеф — в самом конце (точки через `PointToTerrain`, дороги через `RegionToTerrain`).

## Пошагово

### 1. Контур → регион

`SplineToRegion`: `Splines` (замкнутый сплайн), `MaxSegmentLength = 1` → `Result`.
Ресемплит контур в полигон. Меньше `MaxSegmentLength` — глаже кривая, но больше рёбер.

### 2. (опц.) Вырезать препятствия

`PolygonBoolean`: `A` = регион города, `B` = регион препятствия (озеро, скала), `Mode = Difference` → `Result`.
Город обтекает дыру. Несколько препятствий — цепочкой или объединить их в один `B`.

### 3. Нарезать на кварталы

`SubdivideRegion`: `Region`, `MinSize = 20`, `MaxDepth = 6`, `SplitJitter = 0.1`, `Seed` → `Blocks`.
Рекурсивно режет регион пополам (чередуя ось) до `MinSize` или `MaxDepth`. `SplitJitter` сдвигает линию реза от центра (0 — ровная сетка, 0.3 — кривее). Каждое ребро-рез помечается глубиной рекурсии; рёбра исходного контура — глубина 0.

### 4. Назначить класс дорог

`AssignRoadClassByDepth`: `Blocks`, `WidthByDepth` (кривая), `MaxWidth = 8`, `MinDepth = 1`, `MaxDepth = 4` → `Result`.
Каждому ребру-резу глубины `d` в диапазоне `[MinDepth, MaxDepth]` ставит ширину `WidthByDepth.Evaluate(d / MaxDepth) * MaxWidth`.

Ключевые ручки:

- `MaxDepth` здесь — самая глубокая дорога. Ставь меньше, чем `SubdivideRegion.MaxDepth`: режем до 6 (мелкие кварталы под дома), дороги — до 4. Тогда глубокие резы (4, 5) остаются границами кварталов без дорог между ними.
- `MinDepth = 1` — без периметральной дороги. `MinDepth = 0` — по контуру (бывший сплайн) идёт самая широкая дорога.
- `WidthByDepth` — иерархия: слева (глубина 0/малая) проспекты, справа (глубже) — переулки. Дефолт `Linear(0→1, 1→0.2)`.

### 5. Ветка дорог

`BlocksToRoads`: `Blocks` (= `Result` из шага 4), `Join = Round`, `Cap = Butt`, `MiterLimit = 2`. Выход — порт `Roads` (RegionSet), а не отдельная нода. Связывает рёбра-дороги в ломаные по классам ширины и оффсетит их в ленты, объединяя в дорожную сеть. Перекрёстки сливаются сами. `Join` — форма углов, `Cap` — концы лент.

Финального рендера дорог пока нет — см. «Чего ещё нет».

### 6. Ветка кварталов под дома

`InsetRegion`: `Region` (= `Result` из шага 4), `Delta = -(MaxWidth/2 + запас)` → `Result`.
Ужимает кварталы внутрь, чтобы дома не лезли на проезжую часть (дорога центрирована на резе, занимает половину ширины по каждую сторону).

`LotsFromBlock`: `Blocks` (= ужатые кварталы), `LotWidth = 12` → `Lots`.
Режет каждый квартал поперёк вдоль длинной стороны на лоты шириной ~`LotWidth`.

`RegionToPoints`: `Region` = `Lots`, `Roads` = `Roads` (из шага 5), `Mode`, `Count`, `Spacing`, `Seed`, `Margin` → `Results` (List<PointData>).

- `Mode = Centroid` — один дом по центру лота (для плотной фронтальной застройки).
- `Mode = Grid` / `Random` — несколько точек на лот (`Spacing` / `Count`).
- `Margin` — отступ от границ лота (для всех режимов).
- `Roads` подключён → точки разворачиваются лицом к ближайшей дороге (`PointData.Angle`).

### 7. Дома и рельеф

`Results` → weighted-инстансер (выбор префаба дома) → `PointToTerrain` (`Terrain` = TerrainData, `ProjectionMode = Surface`, `ProjectNormal`) — сажает дома на высоту рельефа.

## Чего ещё нет (TODO)

Эти ноды в проекте не созданы — финальный рендер дорог на них и держится:

- `RegionToTerrain` — положить полотно дороги (`Roads`) на террейн. Отдельный ТДД; нужно решить, как именно: покраска splatmap террейна или драпированный меш-декаль.
- `MeshAlongSpline` — лофт профиля вдоль сплайна (`RegionToSpline(Roads)` → бордюр; та же нода — заборы/стены). Отдельный ТДД.

Сейчас дорожная сеть существует только как 2D-`RegionSet` (`Roads`) на плоскости `PlaneY`. «Roads» в графе — это выходной порт `BlocksToRoads`, а не нода.

## Быстрый минимум

Самый короткий город без домов и террейна:

```
Spline → SplineToRegion → SubdivideRegion → AssignRoadClassByDepth → BlocksToRoads
```

## Типичные настройки

- Ровный «манхэттен»: `SplitJitter = 0`, `Join = Miter`.
- Органика: `SplitJitter = 0.2..0.3`, `Join = Round`.
- Только магистрали без мелких улиц: `AssignRoadClassByDepth.MaxDepth = 2..3`.
- Кольцевая дорога по контуру: `AssignRoadClassByDepth.MinDepth = 0`.
- Дома не на дороге: `InsetRegion.Delta` ≈ `-(MaxWidth/2 + 1..2)`.
```
