Status: Не готов

# SplineToTerrain + удаление драпировки из PCG.Sweep — Agent Execution Spec

Нода `Spline To Terrain` (PCG.Splines) проецирует узлы сплайнов на высоту террейна. Из PCG.Sweep террейн-драпировка удаляется полностью: свип становится чисто геометрическим, полотно держит высоты входного сплайна. Пресет `StonePath` переводится на цепочку `Resample Splines → Spline To Terrain → Sweep Spline`.

## References (not inlined)

- Конвенции кода и запреты: `CLAUDE.md` репозитория (табы, типы по файлам, без комментариев, meta руками не трогать, Bridge-таски не удалять).
- Принципы: `Docs/DESIGN_PRINCIPLES.md` (снапшот на главном потоке → расчёт в пуле → применение на главном; `OperationScope`; `GetVersionSalt` для внешних ссылок).
- Skill: `unity-bridge` — все проверки в живом Unity Editor через Bridge-задачи и `bash Assets/Editor/CoworkBridge/wait-for-result.sh`.
- Программное редактирование пресета: Authoring API `PCG.Authoring` (`PcgGraphAuthoring`, `PcgGraphEditSession`); инструкция — `UNITYCOWORK.md` в ядре, находится skill'ом.
- Карты: `Docs/SPLINES_MAP.md`, `Docs/SWEEP_MAP.md`.

## Foundations (shared, used across units)

Контракт пакета PCG.Splines: сплайны в графе — в мировых координатах. Копирование сплайна — только `SplineCopyUtility.CopySpline` (namespace `PCG.Splines.Tools`, сборка `PCG.Splines.Editor`): сохраняет tangent-режимы, tensions и embedded SplineData; копи-конструктор `new Spline(Spline)` НЕ переносит режимы узлов — не использовать.

Новые файлы (финальный код — в Unit 1):

- `Packages/PCG.Splines/Scripts/Splines/SplineToTerrainNode.cs` — data-нода.
- `Packages/PCG.Splines/Editor/Scripts/Exec/SplineToTerrainNodeExecutor.cs` — executor.
- `Packages/PCG.Splines/Editor/Scripts/Exec/SplineTerrainWindow.cs` — окно heightmap (перенос `SweepTerrainWindow` из PCG.Sweep + статический `Capture`).

## Invariants (must hold throughout)

- Правятся только: три новых файла PCG.Splines; перечисленные в Unit 3 файлы PCG.Sweep; `Packages/PCG.Sweep/Presets/StonePath.asset` (только через Authoring-сессию); `Assets/Examples/EditorTools/EditorToolsScene.unity` (только регенерацией); `Docs/SWEEP_MAP.md`, `Docs/SPLINES_MAP.md`, `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md`, `Packages/PCG.Splines/Documentation~/Splines-Addon.md`.
- `Assets/Plugins/PCG4U/**` (релизное ядро) не изменяется.
- Пресет `Packages/PCG.Polygons/Presets/CityBlocks.asset` не изменяется.
- Геометрия свипа без террейна не меняется: формулы вершин `basePos + right * rx + up * ry`, TrimColumns, капы, UV — не трогать; удаляются только террейн-ветки.
- Публичные поля нод, не перечисленные к удалению, не переименовываются.
- `*.meta` руками не редактируются; удаление файла — вместе с его `.meta`.

## Execution Plan

Units run in listed order.

### Unit 1 — Нода SplineToTerrain в PCG.Splines

- Goal: три новых файла созданы с кодом ниже, проект компилируется.
- Touch: три файла из Foundations, только создание.
- How: записать файлы дословно.

`Packages/PCG.Splines/Scripts/Splines/SplineToTerrainNode.cs`:

```csharp
using System.Collections.Generic;
using PCG.GraphModel;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	[PcgNodeInfo("Projects spline knots onto a terrain surface.",
		DisplayName = "Spline To Terrain",
		Category = "Splines",
		Tags = new[] { "spline", "terrain", "project" })]
	public class SplineToTerrainNode : PcgPreviewNode
	{
		[Input]
		[PcgMemberInfo("Splines to project onto the terrain.", Tags = new[] { "spline", "source" })]
		public List<Spline> Splines = new();

		[Input]
		[PcgMemberInfo("Terrain the knots are projected onto; empty keeps the splines unchanged.", Tags = new[] { "terrain" })]
		public TerrainData Terrain;

		[Input]
		[PcgMemberInfo("World-space offset of the terrain.", Tags = new[] { "terrain", "offset" })]
		public Vector3 TerrainOffset;

		[Input]
		[PcgMemberInfo("Vertical offset above the terrain surface.", Tags = new[] { "height", "offset" })]
		public float HeightOffset = 0.1f;

		[Output]
		[PcgMemberInfo("Projected splines.", Tags = new[] { "spline", "results" })]
		public List<Spline> Results => default;
	}
}
```

`Packages/PCG.Splines/Editor/Scripts/Exec/SplineTerrainWindow.cs` — тело класса `SweepTerrainWindow` из `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepTerrainWindow.cs` дословно (поля и `TrySampleHeight`), с заменами: namespace `PCG.Splines`, имя класса `SplineTerrainWindow`, добавить `using UnityEngine;` и статический метод:

```csharp
		public static SplineTerrainWindow Capture(TerrainData terrain, Vector3 origin, float worldMinX, float worldMaxX, float worldMinZ, float worldMaxZ)
		{
			int resolution = terrain.heightmapResolution;
			Vector3 size = terrain.size;

			float txMin = (worldMinX - origin.x) / size.x * (resolution - 1);
			float txMax = (worldMaxX - origin.x) / size.x * (resolution - 1);
			float tzMin = (worldMinZ - origin.z) / size.z * (resolution - 1);
			float tzMax = (worldMaxZ - origin.z) / size.z * (resolution - 1);

			int x0 = math.clamp((int)math.floor(txMin) - 1, 0, resolution - 1);
			int x1 = math.clamp((int)math.ceil(txMax) + 1, 0, resolution - 1);
			int z0 = math.clamp((int)math.floor(tzMin) - 1, 0, resolution - 1);
			int z1 = math.clamp((int)math.ceil(tzMax) + 1, 0, resolution - 1);

			int width = x1 - x0 + 1;
			int height = z1 - z0 + 1;

			var heights = terrain.GetHeights(x0, z0, width, height);

			return new SplineTerrainWindow
			{
				Heights = heights,
				X0 = x0,
				Z0 = z0,
				Width = width,
				Height = height,
				Resolution = resolution,
				SizeX = size.x,
				SizeY = size.y,
				SizeZ = size.z,
				OriginX = origin.x,
				OriginY = origin.y,
				OriginZ = origin.z
			};
		}
```

`Packages/PCG.Splines/Editor/Scripts/Exec/SplineToTerrainNodeExecutor.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.GraphModel;
using PCG.Splines.Tools;
using PCG.Splines.Utilities;
using PCG.Terrains;
using PCG.Utilities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public class SplineToTerrainNodeExecutor : PcgAsyncPreviewNodeExecutor<SplineToTerrainNode>
	{
		public PcgOutput<List<Spline>> Results;

		public override bool IsEmpty => Results.Value == null;

		protected override async UniTask DoComputeAsync(CancellationToken ct)
		{
			Results.Value = new List<Spline>();

			var terrain = GetInputValue(nameof(Data.Terrain), Data.Terrain);
			var terrainOffset = GetInputValue(nameof(Data.TerrainOffset), Data.TerrainOffset);
			var heightOffset = GetInputValue(nameof(Data.HeightOffset), Data.HeightOffset);

			var splinesList = GetInputValues(nameof(Data.Splines), Data.Splines);
			if (splinesList == null || splinesList.Length <= 0)
				return;

			var copies = new List<Spline>();
			float minX = float.MaxValue;
			float minZ = float.MaxValue;
			float maxX = float.MinValue;
			float maxZ = float.MinValue;

			using (var scope = OperationScope.Start(this))
			{
				foreach (var splines in splinesList)
				{
					if (splines == null)
						continue;

					foreach (var spline in splines)
					{
						if (spline == null || spline.Count < 1)
							continue;

						if (terrain == null)
						{
							Results.Value.Add(spline);
							continue;
						}

						var copy = SplineCopyUtility.CopySpline(spline);
						copies.Add(copy);

						for (int i = 0; i < copy.Count; i++)
						{
							float3 p = copy[i].Position;
							minX = math.min(minX, p.x);
							maxX = math.max(maxX, p.x);
							minZ = math.min(minZ, p.z);
							maxZ = math.max(maxZ, p.z);
						}

						await scope.Step(ct: ct);
					}
				}
			}

			if (terrain == null || copies.Count == 0)
				return;

			var window = SplineTerrainWindow.Capture(terrain, terrainOffset, minX, maxX, minZ, maxZ);

			var heights = new float[copies.Count][];
			bool outOfBounds = false;

			await UniTask.SwitchToThreadPool();
			try
			{
				int counter = 0;
				for (int s = 0; s < copies.Count; s++)
				{
					var copy = copies[s];
					var values = new float[copy.Count];
					for (int i = 0; i < copy.Count; i++)
					{
						float3 p = copy[i].Position;
						if (window.TrySampleHeight(p.x, p.z, out float h))
						{
							values[i] = h + heightOffset;
						}
						else
						{
							values[i] = p.y;
							outOfBounds = true;
						}

						counter++;
						if (counter % 1024 == 0)
						{
							ct.ThrowIfCancellationRequested();
							PcgComputeSystem.ReportProgress(this);
						}
					}

					heights[s] = values;
				}
			}
			finally
			{
				await UniTaskEditor.SwitchToEditorThread();
			}

			using (var scope = OperationScope.Start(this))
			{
				for (int s = 0; s < copies.Count; s++)
				{
					var copy = copies[s];
					var values = heights[s];
					for (int i = 0; i < copy.Count; i++)
					{
						var knot = copy[i];
						knot.Position.y = values[i];
						copy.SetKnot(i, knot);
					}

					Results.Value.Add(copy);
					await scope.Step(ct: ct);
				}
			}

			if (outOfBounds)
				Debug.LogWarning("[Spline To Terrain] Part of the splines is outside the terrain and keeps the spline height.");
		}

		public override int GetVersionSalt()
		{
			unchecked
			{
				int hash = 17;
				var terrain = GetInputValue(nameof(Data.Terrain), Data.Terrain);
				if (terrain != null)
					hash = (hash * 397) ^ PcgTerrainContentVersion.Get(terrain);
				return hash;
			}
		}

		public override void DrawPreview(Transform transform)
		{
			var gizmosOptions = GetGizmosOptions();

			Gizmos.color = gizmosOptions.Color;
			SplinesGizmoUtility.DrawGizmos(Results.Value, transform);
		}
	}
}
```

- Если `using PCG.Splines.Utilities` не резолвится (фактический namespace `SplinesGizmoUtility` другой) — открыть `ResampleSplinesNodeExecutor.cs`, взять точный набор usings оттуда и повторить его.
- Gate: Bridge-задача с `AssetDatabase.Refresh` через `wait-for-result.sh` завершается без ошибок компиляции в ответе.
- On failure: ≤3 итерации правок по тексту ошибок компиляции; далее стоп и отчёт. Новые файлы сверх трёх не создавать.

### Unit 2 — Smoke окна высот

- Goal: `SplineTerrainWindow.Capture` + `TrySampleHeight` совпадают с высотой террейна.
- Touch: только временная Bridge-задача.
- How: Bridge-задача: открыть сцену `Assets/Examples/EditorTools/EditorToolsScene.unity`, взять Terrain сцены (`TerrainData` + мировая позиция террейна как origin), построить окно `Capture(data, origin, ox + 50, ox + 250, oz + 50, oz + 250)` (`ox`/`oz` — origin.x/origin.z), в 10 точках сетки внутри этого прямоугольника сравнить `TrySampleHeight(x, z, out h)` с `origin.y + data.GetInterpolatedHeight((x - origin.x) / data.size.x, (z - origin.z) / data.size.z)`. Результат — строка `OK maxDelta=<значение>`.
- Gate: в ответе Bridge `OK maxDelta=` и значение ≤ 0.001.
- On failure: ≤2 правки `Capture`/вызова; далее стоп и отчёт с фактическими дельтами.

### Unit 3 — Удаление террейна из PCG.Sweep

- Goal: в PCG.Sweep нет ни одного упоминания террейна и `HeightOffset`, проект компилируется, геометрия без террейна не изменена.
- Touch и How, по файлам:
	- `Scripts/Sweep/SweepSplineNode.cs` — удалить поля `Terrain`, `TerrainOffset`, `HeightOffset` с их атрибутами.
	- `Editor/Scripts/Exec/SweepTerrainWindow.cs` — удалить файл и его `.meta`.
	- `Editor/Scripts/Exec/SweepSnapshot.cs` — удалить поля `Terrain`, `HeightOffset`.
	- `Editor/Scripts/Exec/SweepNetworkSnapshot.cs` — удалить поле `HeightOffset`.
	- `Editor/Scripts/Exec/SweepMeshData.cs` — удалить поле `TerrainOutOfBounds`.
	- `Editor/Scripts/Exec/SweepSplineNodeExecutor.cs` — удалить `using PCG.Terrains;`; оба метода `CaptureTerrainWindow`; в `BuildSnapshot`: чтение `heightOffset`, переменные bounds (`minX`/`minZ`/`maxX`/`maxZ`/`hasBounds`, цикл их заполнения по кадрам), блок `terrainWindow`, инициализацию `Terrain`/`HeightOffset` снапшота; в `ComputeSingleAsync` и `ComputeNetworkAsync`: локали `terrain`/`terrainOffset`/`heightOffset` и их чтения, флаги `outOfBounds` с `Debug.LogWarning("[Sweep Spline] Part of the sweep is outside the terrain...")`, `mesh.TerrainOutOfBounds`; в вызове `SweepNetworkSolver.BuildNetwork` убрать аргумент `terrain != null`; в `BuildJunctionResults` убрать параметр `ref bool outOfBounds` и строку с ним; в `GetVersionSalt` убрать блок террейна.
	- `Editor/Scripts/Exec/SweepNetworkSolver.cs` — из сигнатуры `BuildNetwork` убрать параметр `bool hasTerrain` (в теле не используется).
	- `Editor/Scripts/Exec/SweepMeshBuilder.cs` — удалить локали `terrain`/`hasTerrain`/`verticalOffsets`/`rightXz`; ветвление позиции вершины заменить единственной строкой `positions[idx] = basePos + right * rx + up * ry;`; удалить блок драпировки (`if (hasTerrain)` с `TrySampleHeight`) и `outOfBounds`; из результата убрать `TerrainOutOfBounds`.
	- `Editor/Scripts/Exec/SweepJunctionMeshBuilder.cs` — удалить локали `terrain`/`hasTerrain`/`heightOffset`; удалить параметр `hasTerrain` из `MakeVertex`/`LoftEdgeVertex`/`CcwIsMax`/`CornerVertex` и всех вызовов, оставив ветку построения `pos` без террейна; удалить финальный блок драпировки вершин (`if (hasTerrain)` с `TrySampleHeight`) и `outOfBounds`/`TerrainOutOfBounds`; удалить террейн-ветку уплотнения в `RimSamples` (блок `if (hasTerrain)` с `Bezier2Length`); удалить midpoint-подразбиение листов, выполняемое только при террейне; после этого удалить каналы вертикальных смещений `ry`/`rv`, единственным потребителем которых была драпировка: out-параметры `ry`/`rv` в `MakeVertex`/`LoftEdgeVertex`/`Ring`/`SampleRim`, массивы `rv`, поле `_rvs` и параметр `rvs` у `SweepJunctionInterpolator` (метод `Sample` возвращает только height). Перед удалением каждого символа проверять поиском по файлу, что других потребителей нет; если у символа есть потребитель вне драпировки — символ оставить и записать в отчёт.
	- `Editor/Scripts/Exec/SweepJunctionInterpolator.cs` — по той же схеме: убрать `_rvs`, параметр конструктора, out `rv`.
- Gate: Bridge-компиляция без ошибок; `grep -rn "errain" Packages/PCG.Sweep/Scripts Packages/PCG.Sweep/Editor` — пусто; `grep -rn "HeightOffset" Packages/PCG.Sweep/Scripts Packages/PCG.Sweep/Editor` — пусто.
- On failure: ≤3 итерации по ошибкам компиляции; если `SweepJunctionMeshBuilder` не сходится за 3 итерации — вернуть файл к исходному состоянию, удалить в нём только финальный блок драпировки и локали `terrain`/`heightOffset` (оставив мёртвые `rv`-каналы), зафиксировать это в отчёте, продолжить.

### Unit 4 — Пресет StonePath на новую цепочку

- Goal: `StonePath.asset` использует `Resample Splines → Spline To Terrain` перед свипом и обочинами, коммит без ошибок валидации.
- Touch: `Packages/PCG.Sweep/Presets/StonePath.asset` — только через `PCG.Authoring`.
- How: Bridge-задача. Найти ассет: `AssetDatabase.FindAssets("StonePath t:PcgSubGraph")` → путь → `LoadAssetAtPath<PcgSubGraph>`. `PcgGraphAuthoring.Begin(host, "StonePath terrain rewire")`; по `GetSnapshot` найти id нод: `SubGraphInputNode` (вход Splines), `SweepSplineNode`, `PointsOffsetSplinesNode`, пилюлю переменной `Terrain` (нода-переменная с портами значения и `Offset` — те же порты, что подключены к `PointToTerrainNode` в этом же графе). Затем в сессии:
	- `AddNode` `ResampleSplinesNode`, `SetParameter` `Step = 2`.
	- `AddNode` `SplineToTerrainNode`, `SetParameter` `HeightOffset = 0.08`.
	- `Disconnect` связи входа Splines к `SweepSplineNode.Splines` и к `PointsOffsetSplinesNode.Splines`.
	- `Connect`: вход Splines → `ResampleSplines.Splines`; `ResampleSplines.Results` → `SplineToTerrain.Splines`; `SplineToTerrain.Results` → `SweepSplineNode.Splines`; `SplineToTerrain.Results` → `PointsOffsetSplinesNode.Splines`.
	- `Connect`: порт значения пилюли `Terrain` → `SplineToTerrain.Terrain`; порт `Offset` пилюли → `SplineToTerrain.TerrainOffset` (имена портов пилюли взять из snapshot по образцу её связей с `PointToTerrainNode`).
	- `Validate` — ошибок нет; `AutoLayout`; `Commit(Save)`.
- Gate: ответ Bridge содержит результат `Commit` со статусом успеха и итоговый список edges, включающий все шесть новых связей.
- On failure: при `SessionAlreadyActive`/ошибке коммита — `Dispose` (откат), одна повторная попытка; далее стоп и отчёт. Руками YAML ассета не править.

### Unit 5 — Регенерация сцены и проверка высот

- Goal: объект `Path` в `EditorToolsScene` генерируется без ошибок, полотно лежит на террейне.
- Touch: `Assets/Examples/EditorTools/EditorToolsScene.unity` — только регенерация; скриншот в `Assets/Examples/EditorTools/Screenshots/02-path-projected.png`.
- How: Bridge-задача: открыть сцену, на объекте `Path` выполнить headless-генерацию через `PcgGraphRunner` (как при сборке сцены — см. `Docs/tdd/260711-2244-TDD-editor_tools_demo.md`, журнал), дождаться завершения; проверить: в консоли нет ошибок; у `Path` есть дочерний mesh-объект с `MeshFilter`/`MeshRenderer`/`MeshCollider`; взять 10 равноудалённых вершин полотна, для каждой вычислить `|v.y - (terrainY(v.x, v.z) + 0.08)|`, где `terrainY` — высота террейна сцены в мировых координатах; результат — `OK maxDev=<значение> vertices=<число>`. Сохранить скриншот SceneView с полотном.
- Gate: ответ Bridge `OK maxDev=` со значением ≤ 0.5 и `vertices=` > 100; ошибок консоли нет.
- On failure: если maxDev > 0.5 — один прогон диагностики (вывести 10 пар `v.y`/`terrainY`), стоп и отчёт; генерацию повторно не крутить более 2 раз.

### Unit 6 — Карты и справка

- Goal: документация соответствует коду.
- Touch: `Docs/SWEEP_MAP.md`, `Docs/SPLINES_MAP.md`, `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md`, `Packages/PCG.Splines/Documentation~/Splines-Addon.md`.
- How: в `SWEEP_MAP.md` убрать из описания и таблицы ноды параметры `Terrain`/`TerrainOffset`/`HeightOffset`, упоминания драпировки, `SweepTerrainWindow`, окна высот и `TerrainOutOfBounds`; в описании пресета `StonePath` отразить цепочку `Resample Splines (Step 2) → Spline To Terrain (HeightOffset 0.08) → Sweep Spline`. В `SPLINES_MAP.md` добавить строку ноды `SplineToTerrainNode` (`Splines, Terrain, TerrainOffset, HeightOffset` → `Results`) и упомянуть `SplineTerrainWindow` в editor-типах. В обоих `Documentation~` обновить/добавить разделы соответствующих нод тем же тоном, что соседние разделы.
- Gate: `grep -n "драпировк" Docs/SWEEP_MAP.md` — пусто; `grep -n "SweepTerrainWindow" Docs/SWEEP_MAP.md` — пусто; `grep -n "SplineToTerrainNode" Docs/SPLINES_MAP.md` — непусто; `grep -n "Spline To Terrain" Packages/PCG.Splines/Documentation~/Splines-Addon.md` — непусто.
- On failure: одна правка по факту grep; далее стоп и отчёт.

## Done (/goal condition)

Все шесть юнитов закрыты и это видно из транскрипта: ответы Bridge-задач без ошибок компиляции; smoke Unit 2 вернул `OK maxDelta=` ≤ 0.001; `grep -rn "errain" Packages/PCG.Sweep/Scripts Packages/PCG.Sweep/Editor` и `grep -rn "HeightOffset"` там же — пусто; Unit 4 закоммичен со списком шести новых связей; Unit 5 вернул `OK maxDev=` ≤ 0.5 при `vertices=` > 100 и чистой консоли; grep-гейты Unit 6 сходятся. Инварианты: `Assets/Plugins/PCG4U/**` и `CityBlocks.asset` не изменены (`git status` не показывает их); `*.meta` не правились руками. Стоп после 40 ходов или при исчерпании лимитов «On failure» любого юнита.

## End-of-run report

- Смени `Status` вверху документа на `Выполнено`.
- Отчитайся: какие юниты закрыты, где потребовались повторы гейтов, на чём остановился и почему (включая символы, оставленные в `SweepJunctionMeshBuilder` по fallback-ветке, если она сработала).
- Флаг, без действий: уточни у заказчика, нужно ли обновлять прочую проектную документацию под эти изменения.
