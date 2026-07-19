# PCG.Octree — пространственный поиск точек

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.Octree`); `Editor/` — исполнители (asmdef `PCG.Octree.Editor`). Зависит от `Octree` (`com.elmortem.octree`), `Unity.Burst`, `Unity.Mathematics`.

## Ноды

| Нода | Назначение | Input → Output |
|---|---|---|
| `PointsNearPointsOctreeNode` | разделить точки на «есть/нет сосед в радиусе» через Octree | `Points, OtherPoints, Radius, WorldCenter, WorldSize, RemoveThemselves, UseScale` → `Results` (без соседей), `NearPoints` (с соседями) |

**Особенности исполнителя:** строит `PointOctree<PointData>` с адаптивным размером узла; батч-обработка (5k/батч); при `RemoveThemselves` — параллельный самопоиск дублей (`UniTask.WhenAll`, до 16 батчей по 100k). Превью рисует куб octree + точки выбранного выхода.
