# PCG.BRG — инстансинг через BatchRendererGroup

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.BRG`); `Editor/` — исполнители (asmdef `PCG.BRG.Editor`). Зависит от `BRG` (`com.elmortem.brg`). Высокопроизводительный рендер множества копий.

## Ноды

| Нода | Назначение | Input → Output |
|---|---|---|
| `GameObjectToBrgNode` | сгруппировать `GameObjectInstanceData` по префабам для BRG | `Enabled, Instances: List<GameObjectInstanceData>` → `Results: List<BrgInstanceData>` |

## Опорные типы
- `BrgInstanceData` (`InstanceData`) — `Prefab` + `List<PointData> Points` (все точки одного префаба в группе).
- `BrgInstanceMaker` (`InstanceMakerBase`) — на каждый префаб создаёт `BrgContainer` (компонент из BRG), бьёт точки на батчи по 65000, заполняет `BrgItem` (позиция/ротация из Normal+Angle/масштаб). Использует `Memcpy.compute` (`MemcpyShader`).
