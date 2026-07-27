# PCG.BRG — инстансинг через BatchRendererGroup

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.BRG`); `Editor/` — исполнители (asmdef `PCG.BRG.Editor`). Зависит от `BRG` (`com.elmortem.brg`). Высокопроизводительный рендер множества копий.

## Ноды

| Нода | Назначение | Input → Output |
|---|---|---|
| `GameObjectToBrgNode` | сгруппировать `GameObjectInstanceData` по префабам для BRG | `Enabled, Instances: List<GameObjectInstanceData>` → `Results: List<BrgInstanceData>` |

## Опорные типы
- `BrgInstanceData` (`InstanceData`) — `Prefab` + `PcgPointCloud Points` (все точки одного префаба в группе). DTO, не порт графа, но облако нужно ради атрибутов: `GameObjectInstanceData` несёт неравномерный масштаб в поле `Scale3`, и `GameObjectToBrgNodeExecutor` кладёт его в колонку `PcgPointAttributes.Scale3` строки только что добавленной точки. Без этого неравномерный масштаб ассембли терялся при рендере через BatchRendererGroup.
- `BrgInstanceMaker` (`InstanceMakerBase`) — на каждый префаб создаёт `BrgContainer` (компонент из BRG), бьёт точки на батчи по 65000, заполняет `BrgItem` (позиция/ротация из Normal+Angle/масштаб). Итоговый масштаб инстанса — `Point.Scale * Points.GetScale3(index)`, тот же контракт, что в ядровом `GameObjectInstanceMaker`. Использует `Memcpy.compute` (`MemcpyShader`).
- Per-instance цвет пока не проброшен: `BrgItem.Color` всегда белый. Чтобы его окрасить, нужен цветовой канал на `GameObjectInstanceData` в ядре — из аддона недостижимо.
