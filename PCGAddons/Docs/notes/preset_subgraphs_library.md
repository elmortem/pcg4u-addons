# Библиотека пресетов-сабграфов

Поставляемые с аддонами пресеты `PcgSubGraph` (`Presets/` внутри пакета). Кладутся `SubGraphNode`'ом на `PcgComponent`; вход сплайнов — через мультипорт `vSplines`, выходы — `o<id>` в `ResultNode.Instances`.

## Конвенции
- **Вход сплайнов** — блекборд-переменная типа `SplinesValue` (`Id/Name = "Splines"`). `SplinesValue.IsArray=true`, поэтому на `SubGraphNode` она становится мультивходовым портом `vSplines` — туда коннектится `SplineNode.Results` (или любой источник `List<Spline>`).
- **Параметры-пилюли** — блекборд-переменные (`TerrainObjectValue`, `MaterialValue`, `GameObjectWeightsValue`, `FloatValue`, `IntValue`); на `SubGraphNode` доступны инлайн-значениями (`VariableValues`) или портами `v<Id>` / `v<Id>_<Port>` (напр. `vTerrain_Terrain`, `vTerrain_Offset`).
- **Выходы** — `SubGraphOutputNode` (`Name` + `InstanceDatasValue`); на `SubGraphNode` это порт `o<NodeId выходной ноды>`. Все выходы инстансов ведут в `ResultNode.Instances`.
- **Мейкеры хоста** — объект-хост обязан нести `GameObjectInstanceMaker` (инстансы-префабы) и `MeshInstanceMaker` (меши дорог/полотна) в `PcgComponent.InstanceMakerComponents`. Без mesh-мейкера полотно/дороги не материализуются.

## Пресеты
| Пресет | Пакет | Выходы | Переменные |
|---|---|---|---|
| `CityBlocks` | PCG.Polygons | `Roads` (меш), `Houses` (инстансы) | `Splines, Terrain, Houses, RoadMaterial, Seed` |
| `StonePath` | PCG.Sweep | `Path` (меш+коллайдер), `Stones` (инстансы) | `Splines, Terrain, PathMaterial, Stones, Width, StoneOffset, Seed` |

Пакетные зависимости для isolated smoke: оба пакета декларируют `com.elmortem.pcg.splines` (пресеты используют `SplinesValue`; `StonePath` — ещё `PointsOffsetSplinesNode`).

## Демо-сцена
`Assets/Examples/EditorTools/EditorToolsScene.unity` — витрина: кистью красим лес (`Forest`/`ForestDemo` + `ForestMask`, поляна-волюм на `Points By Volumes`), замкнутым сплайном растим город (`City` + `CityBlocks`), открытым сплайном кладём тропу (`Path` + `StonePath`). Террейн `EditorToolsTerrain` (500×500×60). Дорожная сеть со стыками (`SplineIntersection` → `SweepNetwork`) — альтернативный инструмент организации дорог для сплайновых осей.
