# PCG.Octree — пространственный поиск точек

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.Octree`); `Editor/` — исполнители (asmdef `PCG.Octree.Editor`). Зависит от `Octree` (`com.elmortem.octree`), `Unity.Burst`, `Unity.Mathematics`.

## Ноды

| Нода | Назначение | Input → Output |
|---|---|---|
| `PointsNearPointsOctreeNode` | разделить точки на «есть/нет сосед в радиусе» через Octree | `Points, OtherPoints, Radius, WorldCenter, WorldSize, RemoveThemselves, UseScale` → `Results` (без соседей), `NearPoints` (с соседями) |

**Область ответственности.** Нода намеренно остаётся радиусной: она отвечает на вопрос «есть ли сосед в радиусе». Прореживание с учётом bounds и приоритета — это `PrunePointsNode` в ядре, а не задача octree-ноды. Переводить `PointsNearPointsOctreeNode` на bounds не нужно — это было бы дублированием ядровой функции.

**Особенности исполнителя:** порты типизированы `PcgPointCloud`; внутри строится плоская тройка `flatPoints`/`flatClouds`/`flatIndices` и `PointOctree<int>` (payload — индекс точки в `flatPoints`, тип дерева сменился с `PointOctree<PointData>`, сам payload читается только через `IsColliding`); адаптивный размер узла; батч-обработка (5k/батч); при `RemoveThemselves` — параллельный самопоиск дублей (`UniTask.WhenAll`, до 16 батчей по 100k) поверх индексов, сборка выхода — `AppendFrom(flatClouds[idx], flatIndices[idx])`. Превью рисует куб octree + точки выбранного выхода.
