# PCG.SpriteShapes — 2D SpriteShape вдоль сплайнов

> Аддон PCG4U. Базовые контракты ядра, раскладку папок и чек-лист новой ноды см. в [`PROJECT_MAP.md`](PROJECT_MAP.md).
>
> Установка: `https://github.com/elmortem/pcg4u-addons.git?path=PCGAddons/Packages/PCG.SpriteShapes#com.elmortem.pcg.spriteshapes/<version>`, где `<version>` — значение из `package.json`. Правила веток, версий и тегов — раздел 9 `PROJECT_MAP.md`.

**Структура аддона:** `Scripts/` — рантайм-ноды и опорные типы (asmdef `PCG.SpriteShapes`); `Editor/` — исполнители (asmdef `PCG.SpriteShapes.Editor`). Зависит от `PCG.Splines`, `Unity.2D.SpriteShape.Runtime`, `Unity.Splines`.

## Ноды

| Нода | Назначение | Input → Output |
|---|---|---|
| `SpriteShapeInstanceNode` | данные SpriteShape из сплайнов | `Splines, Name, SpriteShape, Height` → `Results: List<SpriteShapeInstanceData>` |

## Опорные типы
- `SpriteShapeInstanceData` (`InstanceData`) — `Name`, `Spline`, `SpriteShape`, `Height`.
- `SpriteShapeValue` (`PcgValue`) — обёртка ассета `SpriteShape`.
- `SpriteShapeInstanceMaker` (`InstanceMakerBase`) — создаёт GameObject + `SpriteShapeController`, конвертирует 3D-сплайн (Unity.Splines) в 2D-сплайн (U2D), swap Y/Z, копирует точки/тангенты/режимы, ставит высоту, рефрешит.
