Status: Готов к реализации

# Спайк террейна: сигналы, лок, квантование, Undo, замеры, тайлы

Семь неизвестных Unity-террейна, от которых зависит дизайн стека слоёв T2 (`Docs/Plans/v2/maps/terrain.md` §2 в ProjectPCG, факты — `Docs/Plans/v2/facts/terrain-research.md`, далее `$RES`), закрываются одним стендом в проекте PCGAddons. Результат — записка `Docs/notes/terrain_stack_spike.md` с семью матрицами и решением «идём дальше» по каждому пункту; код стенда после спайка уходит в архив `Docs/notes/terrain_stack_spike/` и из `Assets` удаляется. Код продукта не меняется.

## Стенд

Папка `Assets/Spike/TerrainStackSpike/Editor/`, asmdef `PCG.Terrain.Spike.Editor` (`includePlatforms: Editor`; references `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`), неймспейс `PCG.Terrain.Spike`. Пакет `com.unity.terrain-tools` 5.0.6 уже в проекте; стенд его сборки не использует — кисти эмулируются `UnityEngine.TerrainTools.TerrainPaintUtility` из модуля террейна.

### `TerrainSpikeStand.cs`

```csharp
public sealed class TerrainSpikeStand : IDisposable
{
	public readonly List<Terrain> Terrains = new();
	public Terrain Create(int resolution, Vector3 size, Vector3 position);   // new TerrainData { heightmapResolution = resolution }, size, Terrain.CreateTerrainGameObject(data), position; как в TerrainDetailInstanceMakerTests.CreateTerrain
	public Terrain CreateWithAlphamaps(int resolution, int alphamapResolution, int layers);   // terrainLayers = N временных TerrainLayer с белой Texture2D 4×4
	public void Dispose();                                                     // DestroyImmediate GO, TerrainData, TerrainLayer, текстур
}
```

### `TerrainCallbackLog.cs`, `TerrainCallbackEntry.cs`

```csharp
public sealed class TerrainCallbackEntry { public string Terrain; public string Kind; public string TextureName; public RectInt Region; public bool Synched; public string Stack; }   // Kind: "heightmap" | "texture" | "undoRedo" | "onTerrainChanged" | "objectChange"
public sealed class TerrainCallbackLog : IDisposable
{
	public readonly List<TerrainCallbackEntry> Entries = new();
	public TerrainCallbackLog();            // подписка: TerrainCallbacks.heightmapChanged, TerrainCallbacks.textureChanged, Undo.undoRedoEvent, ObjectChangeEvents.changesPublished (только ChangeAssetObjectProperties по TerrainData стенда)
	public void Attach(Terrain terrain);    // компонент-зонд TerrainChangedProbe (HideAndDontSave) с OnTerrainChanged(TerrainChangedFlags) → запись "onTerrainChanged" с флагами в TextureName
	public void Clear();
	public string ToMarkdown();             // таблица: terrain | kind | texture | region | synched | stack
	public void Dispose();
}
```

`Stack` — три верхних кадра `new StackTrace(true)` вне стенда (имя метода и класс), чтобы различать `SetHeights` и `ScatterInternal`.

### `TerrainStrokeEmulator.cs`

Эмуляция штриха так же, как `PaintHeightTool.OnPaint` ядра редактора (`$RES` §1.4):

```csharp
public static class TerrainStrokeEmulator
{
	public static void RaiseLower(Terrain terrain, Vector2 uv, float brushSize, float strength);   // CalculateBrushTransform → BeginPaintHeightmap(terrain, xform.GetBrushXYBounds()) → GetBuiltinPaintMaterial: _BrushTex = Texture2D.whiteTexture, _BrushParams = (strength*0.01, 0,0,0), SetupTerrainToolMaterialProperties → Graphics.Blit(ctx.sourceRenderTexture, ctx.destinationRenderTexture, mat, (int)TerrainBuiltinPaintMaterialPasses.RaiseLowerHeight) → EndPaintHeightmap(ctx, "Spike Raise")
	public static void PaintTexture(Terrain terrain, Vector2 uv, float brushSize, TerrainLayer layer);   // BeginPaintTexture → PaintTexture pass → EndPaintTexture
	public static void PaintHoles(Terrain terrain, Vector2 uv, float brushSize);                          // BeginPaintHoles → PaintHoles pass → EndPaintHoles
	public static void ApplyDelayed();   // PaintContext.ApplyDelayedActions() — то, что делает MouseUp
	public static void WriteGpu(Terrain terrain, float[,] heights01, RectInt dest, TerrainHeightmapSyncControl sync);   // Texture2D R16 (v·65535/2 как ushort) → RenderTexture формата terrain.heightmapRenderTextureFormat через Graphics.Blit → RenderTexture.active → CopyActiveRenderTextureToHeightmap(src, dest.min, sync) → active = null
}
```

### `TerrainSpikeReport.cs`

`StringBuilder`-обёртка: `Section(string)`, `Table(string[] header, IEnumerable<string[]> rows)`, `Line(string)`, `ToString()`. Все числа `InvariantCulture`.

## Пункты спайка

Каждый пункт — статический класс с `public static string Run()` (возвращает Markdown раздела). Таблица пункта — по карте `terrain.md` §2; ниже только то, что уточняет карту.

### `SpikeCallbacks` (§2.1)

Террейн 513, `drawInstanced` как есть. Для каждого пути: `log.Clear()` → вызов → строки лога. Пути: `SetHeights` патч 33×33; `SetHeightsDelayLOD` патч + отдельно `SyncHeightmap()`; `WriteGpu` с `None` и с `HeightAndLod`; `DirtyHeightmapRegion(rect, None | HeightAndLod)`; `RaiseLower` + `ApplyDelayed`; `SetAlphamaps` патч; `PaintTexture` + `ApplyDelayed`; `DirtyTextureRegion("alphamap", rect, true | false)`; `SetHoles`; `PaintHoles` + `ApplyDelayed`; `SetDetailLayer`; `SetTreeInstances`; Flatten (`RegisterCompleteObjectUndo(data)` + `SetHeights(0,0,full)`); Undo/Redo штриха (`RegisterCompleteObjectUndo(data)` → `SetHeights` патч → `Undo.IncrementCurrentGroup()` → `PerformUndo()` → лог → `PerformRedo()` → лог); то же для эмулированного штриха `RaiseLower` (undo пишет сам `EndPaintHeightmap`). Прокси без `Terrain`: `new TerrainData` с `alphamapResolution = 64` и двумя слоями — `SetAlphamaps`/`GetAlphamaps` round-trip и `GetHeights` без компонента.

Столбцы матрицы: путь | heightmapChanged (регион, synched) | textureChanged (имя, регион, synched) | undoRedoEvent | OnTerrainChanged (флаги) | ObjectChangeEvents.

### `SpikeLock` (§2.2)

(а) `data.hideFlags |= HideFlags.NotEditable` → `RaiseLower`, `SetHeights`, `WriteGpu`: изменились ли высоты (`GetHeights` до/после), были ли исключения или сообщения в консоли (`Application.logMessageReceived`). Сохраняется ли флаг: `AssetDatabase.CreateAsset(data, "Assets/Spike/tmp.asset")` → `AssetDatabase.LoadAssetAtPath` → `hideFlags`; ассет удаляется. (б) Откат на тике: тень = `GetHeights` всего тайла; в `heightmapChanged` копить `RectInt`; в `EditorApplication.update` переписать регион из тени через `WriteGpu(HeightAndLod)` и сравнить `GetHeights` региона с тенью побитно — поэтому у этого пункта сигнатура `Task<string> RunAsync()`: незавершённый `Task` из `TaskCompletionSource` завершается из `update` (мост ждёт `Task` — `CSharpTaskExecutor.cs:78–83` пакета agentbridge). (в) Undo без мусора: `group = Undo.GetCurrentGroup()` → `RaiseLower` → откат → вариант 1 `PerformUndo` и сравнение высот; вариант 2 `Undo.RevertAllDownToGroup(group)` и сравнение; `Undo.GetCurrentGroupName()` до и после.

### `SpikeQuantization` (§2.3)

Террейн 129 при `size.y` ∈ {600, 1000, 2000}, `position.y = 37.5`. Случайные `v` ∈ [0, 32766] (сид 1) → `h = v / 32766f` → 10 циклов «`SetHeights(h)` → `GetHeights` → метры `pos.y + h·size.y` → обратно `round((H − pos.y)/size.y·32766)` → `h`» по трём путям записи: `SetHeights`, `SetHeightsDelayLOD` + `SyncHeightmap`, `WriteGpu(HeightAndLod)`. Записать: число несовпавших сэмплов после 1-го и 10-го цикла, максимальную дельту в метрах; отдельно: пишет ли CPU-путь округление или усечение (`SetHeights(0.49999f / 32766f · 32766f)` против соседних уровней); значение `GetHeights` для `h = 1.0` и для `h = 0.5` (ожидание `32766/32766` и `16383/32766`).

### `SpikeUndo` (§2.4)

`RegisterCompleteObjectUndo(data)` на 1025 / 2049 / 4097 (4097 — ещё с 4 alphamap 4096²): время, сообщения консоли; затем `SetHeights` патча → `PerformUndo()` → высоты вернулись (сравнение). GPU-запись `WriteGpu(None)` → `PerformUndo()` постороннего объекта (пустой GO с `RegisterCreatedObjectUndo`) → высоты региона сохранились или откатились. Чанк-сабассет: `ScriptableObject` с полем `int Version` как сабассет временного ассета → `RegisterCompleteObjectUndo` → правка → `PerformUndo()` → пришли ли `undoRedoEvent` и `ObjectChangeEvents` по нему.

### `SpikeTimings` (§2.5)

`Stopwatch`, медиана из 5 после одного прогрева; GPU-замер закрывается `AsyncGPUReadback.Request(rt).WaitForCompletion()`. Строки таблицы (1)–(10) по карте; (1) — compute-шейдер `SpikeCompose.compute` в папке стенда: 8 слоёв `RFloat` 512² → `lerp`-цепочка по альфе → R16 RT; (2) — `IJobParallelFor` с `[BurstCompile]` по `NativeArray<float>` тех же 8×512²; (9) — `SetDetailLayer` 1024² и `SetTreeInstances` 50 000 на террейне 1025; (10) — из `SpikeUndo`. Разрешения 1025 / 2049 / 4097 там, где строка зависит от тайла; столбец «аллокация» для `GetHeights` — `GC.GetTotalMemory(true)` до/после.

### `SpikeTiles` (§2.6)

Два террейна 513 рядом (`B.position = A.position + (size.x, 0, 0)`), вариант 1 `A.SetNeighbors(null, null, B, null)` + `B.SetNeighbors(A, …)`, вариант 2 `allowAutoConnect = true` + общий `groupingID` + `Terrain.SetConnectivityDirty()`. `TerrainUtils.TerrainMap.CreateFromPlacement(A)` — перечисляет ли оба. `RaiseLower` с uv у правого края A и размером кисти через шов → лог по тайлам с регионами; `GetHeights` последнего столбца A и первого столбца B побитно; `PaintTexture` узкой кистью через шов — какие тексели alphamap обоих тайлов изменились (индексы по краю); `PaintHoles` — то же для holes.

### `SpikeDrawInstanced` (§2.7)

`drawInstanced` у `CreateTerrainGameObject`; у террейна из меню — `EditorApplication.ExecuteMenuItem("GameObject/3D Object/Terrain")`, чтение флага у `Selection.activeGameObject`, затем удаление объекта и созданного меню ассета `TerrainData` (путь из `AssetDatabase.GetAssetPath(terrainData)`). `RaiseLower` при `false` и `true`: число записей лога, `synched`, время `EndPaintHeightmap`.

## Bridge-таски и ручной прогон

- Один таск на пункт: `Task_2610DD_HHMM_spike_<пункт>.cs` с `public static Task<string> Run()` → `return Task.FromResult(SpikeCallbacks.Run())` и так далее (для `SpikeLock` — его незавершённый `Task`). Вывод каждого — раздел записки. Исходники тасков тоже в архив.
- Ручной прогон кистью: меню `Tools/PCG Spike/Terrain Logger On`, `…/Terrain Logger Dump` (пишет `Library/TerrainSpike/manual.md` и очищает), `…/Terrain Logger Off`. Чек-лист в записке: Raise/Lower один штрих на 513 при `drawInstanced` false и true; Paint Texture; Paint Holes; Ctrl+Z; Flatten Tile; Terrain Tools Smooth (пакет в проекте). Пользователь отдаёт `manual.md`, директор вставляет в записку.

## Записка `Docs/notes/terrain_stack_spike.md`

Разделы 1–7 с матрицами, перед каждой — условие «идём дальше» из карты и вердикт (да/нет и что это значит для T2: CPU- или GPU-путь записи, лок = `NotEditable` или только страж, формула квантования, предел снимка Undo, бюджеты §6.3 числами). Раздел 8 — ручной прогон. Раздел 9 — конфигурация (версия Unity, GPU, `copyTextureSupport`, `globalTextureMipmapLimit`). В конце — список `[не проверено]` из `$RES`, которые спайк закрыл, с ссылками на строки.

## Порядок работ

- Стенд: asmdef, `TerrainSpikeStand`, `TerrainCallbackLog`, `TerrainChangedProbe`, `TerrainStrokeEmulator`, `TerrainSpikeReport`; проверка эмулированного штриха на 513 (высоты изменились, `heightmapChanged` пришёл).
- `SpikeCallbacks` → bridge-таск → раздел 1.
- `SpikeLock` → раздел 2.
- `SpikeQuantization` → раздел 3.
- `SpikeUndo` и `SpikeTimings` (`SpikeCompose.compute`, Burst-джоба) → разделы 4–5.
- `SpikeTiles`, `SpikeDrawInstanced` → разделы 6–7.
- Меню логгера; ручной прогон пользователем; раздел 8.
- Записка целиком; архив `Docs/notes/terrain_stack_spike/` (все `.cs`, `.compute`, таски); удаление `Assets/Spike/`; документация.

## Приёмка

- Для каждого пути записи из §2.1, который T2 должен ловить (кисти, Toolbox, Undo), в матрице есть хотя бы один сигнал; для путей без сигнала записано, чем их ловит T2 (`Verify` по хешу).
- Формула квантования с нулевым дрейфом за 10 циклов хотя бы по одному пути записана; известно, округляет или усекает `SetHeights`.
- Таблица замеров заполнена на 1025 / 2049 / 4097; в записке решение «CPU или GPU композиция» и бюджеты для `terrain.md` §6.3.
- Два тайла: граничный столбец равен побитно; модель текселей alphamap на шве записана.
- В `Assets/` после спайка ничего не осталось; `agentbridge compile` зелёный; `Library/TerrainSpike` не коммитится.

## Документация

- `Docs/notes/terrain_stack_spike.md` — сама записка; `Docs/notes/terrain_stack_spike/` — архив.
- `Docs/Plans/v2/maps/terrain.md` (ProjectPCG): «Изменения после 2026-10-01» — ссылка на записку и обновлённые числа §6.3; `tracks/T-terrain.md` T1 — «выполнено» с вердиктами по 7 пунктам.
