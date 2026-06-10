# ТДД: Миграция аддонов на PcgRandom

Status: Выполнено

Выполняется после ТДД ядра `260610-1549-TDD-pcg_review_fixes` и обновления релизной сборки PCG4U в `Assets/Plugins/PCG4U` (там появляется `PcgRandomExtensions` и исчезает `RandomUtility`). Папку `Assets/Plugins/PCG4U` не трогать.

---

## Паттерн замены

В каждом методе, где был `RandomUtility.PushSeed(seed)` … `RandomUtility.PopSeed()`:

- удалить `PushSeed`/`PopSeed`;
- в начале метода завести `var random = PcgRandom.Create(seed);`;
- удалить локальный фолбэк `seed = UnityEngine.Random.Range(1, int.MaxValue);` перед созданием `random`, если он есть — `PcgRandom.Create` сам обрабатывает `seed <= 0`;
- заменить вызовы по таблице:

| Было | Стало |
|---|---|
| `RandomUtility.Range(float a, float b)` | `random.NextFloat(a, b)` |
| `RandomUtility.Range(int a, int b)` | `random.NextInt(a, b)` |
| `RandomUtility.Range(Vector2 v)` | `random.NextFloat(v.x, v.y)` |
| `RandomUtility.Range(Vector2Int v)` | `random.NextInt(v.x, v.y)` |
| `RandomUtility.Range01()` | `random.NextFloat()` |

Если `random` передаётся во вложенный синхронный метод — параметром `ref Unity.Mathematics.Random random`. Типы `PcgRandom` и расширения — в `PCG.Utilities` (`using PCG.Utilities;`).

Материализацию `Data.Seed = UnityEngine.Random.Range(1, int.MaxValue)` в `OnBind` не трогать.

---

## Файлы и методы

### `Packages/PCG.Mazes/Editor/MazeMstGraphNodeExecutor.cs`

- Строка 43 — удалить фолбэк `seed = Random.Range(1, int.MaxValue);`.
- Строки 45–63 — `PushSeed`/`PopSeed` → `var random = PcgRandom.Create(seed);`; строка 51 `edge.Weight = RandomUtility.Range01();` → `edge.Weight = random.NextFloat();`.

### `Packages/PCG.Splines/Scripts/Surfaces/SplinePoints.cs`

- `GetSurfaceRandomPoints` (строки 105–127): `PushSeed`/`PopSeed` → локальный `random`; строка 114 `spline.Evaluate(RandomUtility.Range01(), ...)` → `spline.Evaluate(random.NextFloat(), ...)`.
- `GetVolumeRandomPoints` (строки 129–160): `PushSeed`/`PopSeed` → локальный `random`; строки 149–150 → `new Vector3(random.NextFloat(bounds.min.x, bounds.max.x), bounds.center.y, random.NextFloat(bounds.min.z, bounds.max.z))`.

### `Packages/PCG.Splines/Editor/Scripts/Exec/RandomSplineNodeExecutor.cs`

- Строка 55 — удалить фолбэк `seed = UnityEngine.Random.Range(1, int.MaxValue);`.
- Строки 57–101 — `PushSeed`/`PopSeed` → локальный `random`; строка 81 `RandomUtility.Range(height)` → `random.NextFloat(height.x, height.y)`; строка 83 `RandomUtility.Range01()` → `random.NextFloat()`.

### `Packages/PCG.Splines/Editor/Scripts/Exec/SplineAroundPointsNodeExecutor.cs`

- Строка 43 — удалить фолбэк `seed = UnityEngine.Random.Range(1, int.MaxValue);`.
- Строки 45–86 — `PushSeed`/`PopSeed` → локальный `random`; строка 68 `RandomUtility.Range(radius)` → `random.NextFloat(radius.x, radius.y)`.

### `Packages/PCG.Splines/Editor/Scripts/Exec/SplinesSurfaceNodeExecutor.cs`

- Строка 47 — удалить фолбэк `seed = Random.Range(1, int.MaxValue);` (сид уходит в `SplinePoints`, который теперь сам корректно обрабатывает `seed <= 0`).

---

## Проверка

- Поиск `RandomUtility` по папке `Packages` не находит ничего.
- Проект компилируется с обновлённой релизной сборкой PCG4U.
- Граф с `RandomSpline`/`SplineAroundPoints`/`MazeMstGraph` и spline-surface нодами в random-режимах генерирует одинаковый результат при повторном вычислении с тем же `Seed`.

---

## После выполнения

- Поменяй статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта (`Docs/PROJECT_MAP.md`) под выполненные изменения.
