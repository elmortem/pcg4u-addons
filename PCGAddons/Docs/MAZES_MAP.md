# PCG.Mazes — графы и лабиринты

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.Mazes`); `Editor/` — исполнители (asmdef `PCG.Mazes.Editor`). Зависит от `PCG.Splines`, `TriangulationDelone` (Делоне), `Unity.Splines`.

## Ноды

| Нода | Назначение | Input → Output |
|---|---|---|
| `GridGraphNode` | граф-сетка | `Size: Vector2Int, CellSize: Vector2` → `Result: Graph, CenterPoints: List<PointData>` |
| `DeloneGraphNode` | граф триангуляции Делоне по точкам | `Points, MinDistance, MinRatio` → `Result: Graph, CenterPoints` |
| `MazeMstGraphNode` | лабиринт через MST (алгоритм Прима) | `Graph, Seed` → `Result: Graph, EndPoints: List<PointData>` |
| `GraphMinusGraphNode` | вычитание графов (удаление пересекающихся рёбер) | `Graph, Minus` → `Result: Graph` |
| `GraphToSplineNode` | рёбра графа → bezier-сплайны | `Graph, AutoSmooth` → `Splines: List<Spline>` |

## Опорные типы (`Scripts/Graphs/`)
- `Graph` — контейнер `List<GraphNode>` + `List<GraphEdge>` (методы FindNode/FindEdge/Clear). Это **value-тип, передаваемый между нодами**.
- `GraphNode` — вершина: `Vector2 Point` + список рёбер. `GraphEdge` — ребро (две вершины + `Weight`).
- `GraphBuilder` — `BuildGraph()` (из треугольников Делоне), `BuildGrid()` (из параметров сетки).
- `MazeGenerator` — генерация лабиринта (Prim's MST).
- `GraphGizmoUtility` — отрисовка графа (2D→3D, Y=0) в превью.
