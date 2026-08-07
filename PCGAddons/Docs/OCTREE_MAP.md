# PCG.Octree — пространственный поиск точек

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.Octree`); `Editor/` — исполнители (asmdef `PCG.Octree.Editor`); `Tests/Editor/` — EditMode-тесты солверов (asmdef `PCG.Octree.Tests`). Зависит от `Octree` (`com.elmortem.octree`), `Unity.Burst`, `Unity.Mathematics`.

## Ноды

| Нода | Назначение | Input → Output |
|---|---|---|
| `PointsNearPointsOctreeNode` | разделить точки на «есть/нет сосед в радиусе» через Octree | `Points, OtherPoints, Radius, WorldCenter, WorldSize, RemoveThemselves, UseScale` → `Results` (без соседей), `NearPoints` (с соседями) |
| `PruneOverlappingPointsNode` | взаимное разрешение пересечений между слоями инстансов по приоритету портов | `In0..In3` (PointSet), `Radius0..Radius3`, `SelfPrune0..SelfPrune3`, `Overlap` → `Out0..Out3` |

**PruneOverlappingPoints.** Приоритет = индекс порта: `In0` сильнейший, он никогда не прунится об остальные слои (и не прунит себя, пока `SelfPrune0=false`). Эффективный радиус точки = `RadiusN × max(scale.x, scale.z)`. Пара конфликтует, если дистанция XZ меньше `Overlap × (r1 + r2)`. Солвер (`OverlapPruneSolver`) сортирует кандидатов (`PruneCandidate`) по приоритету asc, затем по эффективному радиусу desc, и принимает точку, если она не конфликтует ни с одной уже принятой; поиск соседей — через ту же octree-инфраструктуру, что и `PointsNearPointsOctreeNode`. Выходы раскладывают принятые точки обратно по исходным портам с сохранением атрибутов. Типовое применение — финальное согласование слоёв демки: дома (R5) → деревья (R1.6) → кусты (R0.9) → ground cover (R0.35).

**Область ответственности.** Нода намеренно остаётся радиусной: она отвечает на вопрос «есть ли сосед в радиусе». Прореживание с учётом bounds и приоритета — это `PrunePointsNode` в ядре, а не задача octree-ноды. Переводить `PointsNearPointsOctreeNode` на bounds не нужно — это было бы дублированием ядровой функции.

**Особенности исполнителя:** порты типизированы `PcgPointCloud`; внутри строится плоская тройка `flatPoints`/`flatClouds`/`flatIndices` и `PointOctree<int>` (payload — индекс точки в `flatPoints`, тип дерева сменился с `PointOctree<PointData>`, сам payload читается только через `IsColliding`); адаптивный размер узла; батч-обработка (5k/батч); при `RemoveThemselves` — параллельный самопоиск дублей (`UniTask.WhenAll`, до 16 батчей по 100k) поверх индексов, сборка выхода — `AppendFrom(flatClouds[idx], flatIndices[idx])`. Превью рисует куб octree + точки выбранного выхода.
