# ТДД: Sweep Network — фикс потоков солвера

Status: Выполнено

`SweepNetworkSolver.Solve` вызывается из пула потоков, а внутри строит куски через `Spline.Add` и оценивает кадры через `Spline.Evaluate*` — это Unity API, аллоцирующий `NativeArray` с `Allocator.Temp`, разрешённый только на главном потоке. Итог — `ArgumentException: Could not allocate native memory` и падение вычисления. Лечение: солвер делится на пуловую фазу (чистый `SplineSplitSolver`) и главнопоточную фазу (сборка кусков, оценка кадров, setback-математика) с кооперативным `OperationScope`.

---

## Файлы

Изменяются:

- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepNetworkSolver.cs` — `Solve` разделяется на `SolveSplit` и `BuildNetwork`.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepNetworkNodeExecutor.cs` — переключения потоков вокруг двух фаз.

---

## Солвер: две фазы

`Solve` удаляется. Вместо него два метода с тем же суммарным поведением и теми же вычислениями:

- `internal static SplineSplitResult SolveSplit(SplineSnapshot[] snapshots, SplineNetworkTopology topology, CancellationToken ct, Action reportProgress)` — только вызов `SplineSplitSolver.Solve(snapshots, topology.Cuts, пустые точки, 0f, ct, reportProgress)`. Никакого Unity API. Выполняется в пуле.
- `internal static SweepNetworkSolveResult BuildNetwork(List<Spline> flatSplines, SplineSplitResult split, SplineNetworkTopology topology, float2[] profilePoints, float lateralExtent, float setbackScale, CancellationToken ct)` — всё остальное из прежнего `Solve` без изменений логики: сборка `Spline` кусков из инструкций (`Spline.Add`), длины (`GetLength`), привязка концов, два прохода оценки кадров (`EvaluatePosition/Tangent/UpVector`, `ConvertIndexUnit`), азимуты, митры, setback, диапазоны, `SweepNetworkJunction[]`. Выполняется строго на главном потоке.

Внутри `BuildNetwork` никаких `ThrowIfCancellationRequested`-батчей по 1024 не требуется — объём работы пропорционален числу кусков; одна проверка `ct.ThrowIfCancellationRequested()` на кусок и одна на junction.

---

## Executor: последовательность потоков

В `DoComputeAsync` порядок фаз заменяется на:

- Главный поток, `OperationScope`: резолв профиля, параметры, flatten, `SplineSnapshot.Capture` всех сплайнов (`await scope.Step(ct: ct)` после каждого сплайна — как в `SplitSplinesNodeExecutor`).
- Пул (`UniTask.SwitchToThreadPool` → `try/finally` c `UniTaskEditor.SwitchToEditorThread`, по образцу `SplitSplinesNodeExecutor.DoComputeAsync`): `SweepNetworkSolver.SolveSplit`.
- Главный поток, новый `OperationScope`: `SweepNetworkSolver.BuildNetwork`, затем `SweepNetworkFrames.BuildRangeFrames` по кускам (`await scope.Step(ct: ct)` после каждого куска), затем захват окна террейна.
- Пул: параллельные `SweepMeshBuilder.Build` и `SweepJunctionMeshBuilder.Build` — без изменений.
- Главный поток: сборка результатов и `SyncSceneAsync` — без изменений.

Прочая логика executor не меняется.

---

## Приёмка

- X-перекрёсток из двух сплайнов: вычисление проходит без исключений, в консоли нет `Allocator.Temp` и `Could not allocate native memory`; ленты с отступами и патч на месте.
- Правка сплайна во время вычисления: отмена и перезапуск без исключений и без частичных объектов в сцене.
- Bridge-задача `Task_SweepNet_U3` из `260718-1903-TDD-sweep_network_junctions.md` адаптируется к новой паре методов (`SolveSplit` + `BuildNetwork` вызываются последовательно на главном потоке задачи) и проходит со всеми прежними `PASS`-ассертами — численные значения ассертов не меняются.
- Детерминизм: результат повторного вычисления идентичен.

---

## После выполнения

- Смени статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта под эти изменения.
