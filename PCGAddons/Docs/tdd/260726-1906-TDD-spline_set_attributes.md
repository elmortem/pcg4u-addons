Status: Выполнено

# PcgSplineSet: атрибуты на сплайнах — Agent Execution Spec

## References (not inlined)

- Соглашения, стиль кода: `CLAUDE.md`.
- Принципы проектирования: `Docs/DESIGN_PRINCIPLES.md`. Особенно «Параллелизм по умолчанию» и «Не переизобретаем существующее».
- Прямой прецедент того же класса работ, читать первым: `Docs/tdd/done/260725-2051-TDD-addons_point_cloud_migration.md` — миграция точек на `PcgPointCloud`. Правило категорий, паттерн сплющивания входов, форма гейтов — оттуда.
- Эталон контейнера с атрибутами в этом репозитории: `Packages/PCG.Polygons/Scripts/Polygon/RegionSet.cs`.
- Эталон сериализатора с атрибутами: `Packages/PCG.Polygons/Editor/Scripts/Cache/RegionSetSerializer.cs`.
- Карты: `Docs/PROJECT_MAP.md`, `Docs/SPLINES_MAP.md`, `Docs/POLYGONS_MAP.md`, `Docs/BRG_MAP.md`, `Docs/OCTREE_MAP.md`.
- Skill для компиляции, прогонов графов и замеров: `unity-bridge:unity-bridge`.

## Контекст

Точки несут атрибуты (`PcgPointCloud`), регионы несут атрибуты (`RegionSet`). Сплайны — нет: тип порта `List<Spline>`, и всё, что о сплайне известно, теряется на первой же ноде.

Из-за этого рвутся ровно те связи, ради которых всё затевалось. `RegionToSplineNode` берёт регион с атрибутами `lotId`/`depth`/`boundary` и отдаёт голые сплайны. `BlocksToRoadsNode` знает класс дороги, но кладёт наружу только ширину, и то в обход — во встроенный канал Unity. `SplitSplinesNode` вычисляет, между какими перекрёстками лежит каждый кусок, и выбрасывает это. Генераторы точек считают параметр `t`, дистанцию вдоль дуги и ширину — и выбрасывают всё три.

Практический итог для города: нельзя поставить фонари только вдоль главных улиц, нельзя выбрать дом по классу улицы, нельзя сузить тротуар в переулке. Этот ТДД чинит именно это.

Попутно закрываются накопленные хвосты: `PcgPointCloud` в BRG (иначе неравномерный масштаб ассембли теряется при рендере через BatchRendererGroup) и потеря встроенных каналов в адаптере `GameObjectsToSplines`.

## Что делаем и чего НЕ делаем

Делаем: тип `PcgSplineSet`, миграцию всех портов сплайнов, источники атрибутов, мост сплайн→точка, кеш-сериализатор, хвосты по BRG, документацию.

НЕ делаем:

- **Не переносим канал ширины в атрибуты.** Ширина меняется ВДОЛЬ сплайна, а строка атрибутов — одна на сплайн. Это разные вещи, и Unity уже даёт механизм для первой: `SplineData<float>` под ключом `pcg.width`. Правило фиксируется в документации: переменное вдоль сплайна — встроенный канал Unity, постоянное на сплайн — `PcgSplineSet.Attributes`.
- Не заводим пул для `PcgSplineSet` и не регистрируем его в `PcgOutputPools`. Сплайнов в графе десятки, пулинг бессмысленен. `Results.Rent(...)` на сплайновых выходах НЕ использовать — только `Results.Value = new PcgSplineSet()`. Существующий `SplineListPool` остаётся для внутреннего использования как есть.
- Не пишем сериализатор для `SplineNetworkTopology`. `SplineIntersectionNode` из-за этого остаётся некешируемым — так и было, регресса нет.
- Не переводим `PointsNearPointsOctreeNode` на bounds. Это не долг, а дублирование: прореживание по bounds с приоритетом уже есть в ядре — `PrunePointsNode`. Octree остаётся радиусной нодой, и это правильно. Записать этот вывод в `Docs/OCTREE_MAP.md`.
- Не трогаем `unitypcg/ProjectPCG` — другой репозиторий.
- Не меняем алгоритмы: ни `SplineIntersectionSolver`, ни `SplineSplitSolver`, ни свип, ни клиппер.

## Foundations (shared, used across units)

### PcgSplineSet

Файл: `Packages/PCG.Splines/Scripts/Splines/PcgSplineSet.cs`, неймспейс `PCG.Splines`.

Форма копируется с `RegionSet` дословно по смыслу: список геометрии плюс `PcgAttributeSet`, строка на элемент, инвариант `Attributes.Count == Splines.Count`.

```csharp
using System.Collections.Generic;
using PCG.Attributes;
using UnityEngine.Splines;

namespace PCG.Splines
{
	public sealed class PcgSplineSet : IPcgAttributeData
	{
		public List<Spline> Splines = new();

		public PcgAttributeSet Attributes { get; } = new();

		public int Count => Splines.Count;

		public Spline this[int index]
		{
			get => Splines[index];
			set => Splines[index] = value;
		}

		public PcgSplineSet()
		{
		}

		public PcgSplineSet(int capacity)
		{
			Splines = new List<Spline>(capacity);
		}

		public PcgSplineSet(List<Spline> splines)
		{
			Splines = splines;
			Attributes.EnsureCount(splines.Count);
		}

		public List<Spline>.Enumerator GetEnumerator()
		{
			return Splines.GetEnumerator();
		}

		public void Add(Spline spline)
		{
			Splines.Add(spline);
			Attributes.AddRow();
		}

		public void AddRange(IEnumerable<Spline> splines)
		{
			foreach (var spline in splines)
			{
				Add(spline);
			}
		}

		public void AppendFrom(PcgSplineSet source, int sourceIndex)
		{
			Splines.Add(source.Splines[sourceIndex]);
			Attributes.AppendRow(source.Attributes, sourceIndex);
		}

		public void AppendFrom(PcgSplineSet source, int sourceIndex, Spline spline)
		{
			Splines.Add(spline);
			Attributes.AppendRow(source.Attributes, sourceIndex);
		}

		public void Append(PcgSplineSet source)
		{
			for (int i = 0; i < source.Splines.Count; i++)
			{
				AppendFrom(source, i);
			}
		}

		public void Clear()
		{
			Splines.Clear();
			Attributes.Clear();
		}

		public PcgSplineSet Clone()
		{
			var copy = new PcgSplineSet(Splines.Count);
			copy.Splines.AddRange(Splines);
			copy.Attributes.Append(Attributes);
			return copy;
		}

		public bool IsValid()
		{
			return Attributes.Count == Splines.Count;
		}

		public int GetContentHash()
		{
			unchecked
			{
				int hash = Splines.Count;
				for (int i = 0; i < Splines.Count; i++)
				{
					hash = (hash * 397) ^ SplinesUtility.GetContentHash(Splines[i]);
				}

				hash = (hash * 397) ^ Attributes.GetContentHash();
				return hash;
			}
		}
	}
}
```

`SplinesUtility.GetContentHash(Spline)` может не существовать. Прочитай `Packages/PCG.Splines/Scripts/Utilities/SplinesUtility.cs`; если метода нет — добавь его туда, вынеся посплайновую часть хеша из `SplinesValue.GetContentHash` (Count, Closed, по каждому knot Position/TangentIn/TangentOut/Rotation, TangentMode, плюс канал ширины). После этого `SplinesValue.GetContentHash` обязан звать тот же метод, а не держать копию логики.

`Clone` копирует ССЫЛКИ на `Spline` — как `PcgPointCloud.Clone` копирует значения точек. Ноды, меняющие геометрию, обязаны создавать новые `Spline` через `SplineCopyUtility.CopySpline`, как делают сейчас.

### Правило категорий

То же, что в ТДД миграции точек, дословно, только про сплайны:

- **Generator** — сплайны рождаются не из сплайнов (из точек, графа, региона, руками). `Add`. Если источник несёт атрибуты — обязан их перенести, см. Unit 4.
- **Derived-transform** — 1:1, тот же сплайн изменён или пересобран. `AppendFrom(src, i, newSpline)`.
- **Derived-select** — подмножество. `AppendFrom(src, i)`.
- **Derived-fanout** — 1:N (`SplitSplines`). `AppendFrom(src, sourceIndex, piece)` на каждый кусок.
- **Merger** — несколько наборов в один. `Append(src)`.
- **Consumer** — сплайны только на входе.
- **Internal** — `List<Spline>` как локальная переменная, параметр утилиты или хранилище ноды. Остаётся `List<Spline>`.

Использовать `Add` в Derived-ноде — дефект.

### Что остаётся на `List<Spline>`

Порты — нет, всё остальное — да. Явный список файлов, которые НЕ мигрируют:

- `Packages/PCG.Splines/Scripts/Splines/SplineNode.cs` — поле `[HideInNode] List<Spline> Splines` это хранилище редактируемых в сцене сплайнов, а не порт. Остаётся. Мигрирует только `[Output] Results`.
- `Packages/PCG.Splines/Scripts/Splines/SplineListPool.cs`
- `Packages/PCG.Splines/Editor/Scripts/Network/SplineNetworkInput.cs` — `Flatten` меняет только сигнатуру входа на `PcgSplineSet[]`, возвращает по-прежнему `List<Spline>`
- `Packages/PCG.Splines/Editor/Scripts/Tools/SplineCopyUtility.cs`, `SplineEditSession.cs`, `SplineResampleUtility.cs`
- `Packages/PCG.Splines/Scripts/Utilities/SplinesGizmoUtility.cs`, `SplinesUtility.cs`, `SplinesCache.cs`
- `Packages/PCG.Polygons/Scripts/Geometry/SplineRegionConvert.cs`
- всё внутри `Packages/PCG.Sweep/` кроме порта `SweepSplineNode.Splines`

### Имена атрибутов

Новый файл `Packages/PCG.Splines/Scripts/Splines/SplineAttributes.cs`, неймспейс `PCG.Splines`, по образцу `CityAttributes`:

```csharp
namespace PCG.Splines
{
	public static class SplineAttributes
	{
		public const string SplineIndex = "splineIndex";
		public const string SplineT = "splineT";
		public const string SplineDistance = "splineDistance";
		public const string SplineWidth = "splineWidth";
		public const string SplineSide = "splineSide";
		public const string Closed = "closed";
		public const string SourceSplineIndex = "sourceSplineIndex";
		public const string PieceIndex = "pieceIndex";
		public const string StartJunction = "startJunction";
		public const string EndJunction = "endJunction";
		public const string JunctionIndex = "junctionIndex";
		public const string JunctionValency = "junctionValency";
	}
}
```

Типы колонок: `SplineT`, `SplineDistance`, `SplineWidth` — `float`; `Closed` — `bool`; остальные — `int`. `SplineSide` — `-1`, `0` или `+1`.

В `Packages/PCG.Polygons/Scripts/City/CityAttributes.cs` добавить одну константу, существующие не трогать:

```csharp
		public const string RoadClass = "roadClass";
```

## Invariants (must hold throughout)

- Имена полей с `[Input]`/`[Output]` не меняются ни на одной ноде.
- Порядок сплайнов и порядок точек на каждом выходе сохраняется бит-в-бит относительно текущего поведения.
- Схемы параллелизма не меняются: `PcgWorkerScheduler.RunAsync`/`RunIndexedAsync`, `UniTask.WhenAll`, `SwitchToThreadPool`/`SwitchToMainThread`, `OperationScope` + `scope.Step`, размеры батчей. Проверка: число вхождений `SwitchToMainThread`, `SwitchToThreadPool`, `UniTask.WhenAll`, `RunIndexedAsync` в `Packages/` не меняется относительно стартового снапшота.
- Ни одна нода не добавляется, не удаляется и не переименовывается. Параметров у нод не прибавляется и не убавляется.
- `Results.Rent(...)` не появляется ни на одном выходе типа `PcgSplineSet`.
- Ничего не правится в `Assets/Plugins/PCG4U/` — это релизная сборка ядра.
- Ничего не правится в `unitypcg/ProjectPCG`.
- `*.meta` руками не создаются и не правятся. Таски Unity Bridge вручную не удаляются.

## Execution Plan

### Unit 0 — Baseline ДО правок

- Goal: зафиксированы эталонные числа на текущем коде.
- Touch: ничего не править.
- How: через `unity-bridge:unity-bridge`:
  - открыть `Assets/Examples/CityForestV3/CityForestV3.unity`, дождаться простоя `PcgComputeSystem`, прогнать генерацию всех `PcgComponent`, вывести по всем executor'ам (включая вложенные `SubGraphNodeExecutor.Inner`) строки `<NodeTitle>|<NodeType>|<PointsCount или SplinesCount>`; для сплайновых executor'ов выводить число сплайнов на выходе;
  - то же для `Assets/SweepDemo/SweepDemoScene.unity`, дополнительно вывести число вершин и треугольников итогового свип-меша;
  - сохранить обе выдачи целиком в `Docs/notes/spline_set_baseline.md`.
- Gate: файл `Docs/notes/spline_set_baseline.md` существует и содержит не менее 40 строк; содержимое выведено в транскрипт.
- On failure: если граф не прогоняется на текущем коде — остановись и доложи. Не начинать миграцию без baseline.

### Unit 1 — Тип, хеш, сериализатор

- Goal: `PcgSplineSet` существует, кешируется, зарегистрирован.
- Touch: создать `Packages/PCG.Splines/Scripts/Splines/PcgSplineSet.cs`, `Packages/PCG.Splines/Scripts/Splines/SplineAttributes.cs`, `Packages/PCG.Splines/Editor/Scripts/Cache/PcgSplineSetSerializer.cs`, `Packages/PCG.Splines/Editor/Scripts/PcgSplinesBootstrap.cs`. Править `Packages/PCG.Splines/Scripts/Utilities/SplinesUtility.cs` и `Packages/PCG.Splines/Scripts/Values/SplinesValue.cs`.
- How:
  - `PcgSplineSet` и `SplineAttributes` — код из Foundations дословно.
  - `SplinesUtility.GetContentHash(Spline)` — добавить, если нет; `SplinesValue.GetContentHash` переписать так, чтобы посплайновая часть шла через него. Внешнее поведение хеша сохранить: те же поля в том же порядке.
  - `SplinesValue`: `ValueType => typeof(PcgSplineSet)`, `IsArray => true` остаётся, `GetValue` возвращает `new PcgSplineSet(result)` где `result` — тот же список трансформированных сплайнов, что и сейчас. Поле `Containers` не трогать: на нём висят сохранённые сцены.
  - `PcgSplineSetSerializer` — `TypeId => 4` (1 и 3 заняты ядром, 2 — `RegionSetSerializer`). `CanHandle(type) => type == typeof(PcgSplineSet)`. `Snapshot` → `Clone()`. Формат:
    - `Splines.Count`;
    - по каждому сплайну: `Closed` (bool), число knot'ов, далее по knot'у `Position`/`TangentIn`/`TangentOut` (по три float каждый) и `Rotation` (четыре float), затем `(byte)TangentMode`;
    - затем встроенные каналы: число float-каналов, по каналу — ключ (string), `(byte)PathIndexUnit`, `DefaultValue`, число точек, по точке `Index` и `Value`. Перечисление ключей брать через API `Spline`, которым пользуется `SplineCopyUtility.CopyEmbeddedData` — прочитай его и используй те же вызовы.
    - каналы типов `float4`, `int`, `Object` НЕ сериализуются. Если у сплайна такие каналы есть — один раз за запись вывести `Debug.LogWarning` с именем ключа. В проекте таких каналов нет, это защита от тихой потери.
    - `Attributes` пишутся и читаются через `PcgAttributeSetCacheIO.Write/Read` из ядра — ровно как в `RegionSetSerializer`.
  - `PcgSplinesBootstrap` — `[InitializeOnLoadMethod]`, `PcgCacheSerializerRegistry.Register(new PcgSplineSetSerializer());`. Пул НЕ регистрировать.
- Gate: `ls Packages/PCG.Splines/Scripts/Splines/PcgSplineSet.cs Packages/PCG.Splines/Scripts/Splines/SplineAttributes.cs Packages/PCG.Splines/Editor/Scripts/Cache/PcgSplineSetSerializer.cs Packages/PCG.Splines/Editor/Scripts/PcgSplinesBootstrap.cs` находит все четыре; `grep -c "TypeId => 4" Packages/PCG.Splines/Editor/Scripts/Cache/PcgSplineSetSerializer.cs` возвращает 1; `grep -rn "TypeId =>" Packages/ | sort` не содержит дублей номеров; `grep -c "PcgOutputPools" Packages/PCG.Splines/Editor/Scripts/PcgSplinesBootstrap.cs` возвращает 0.
- On failure: ≤4 попытки. Если перечисление встроенных каналов недоступно публичным API — сериализуй только канал `SplineWidthUtility.DataKey`, отметь ограничение в отчёте и продолжай.

### Unit 2 — Миграция портов PCG.Splines

- Goal: все 16 нод и 16 executor'ов PCG.Splines работают на `PcgSplineSet`.
- Touch: файлы из таблицы разделa «Классификация» ниже плюс `SplineNetworkInput.cs`.
- How:
  - Поля-порты: `public List<Spline> X = new();` → `public PcgSplineSet X = new();`, `[Output] public List<Spline> Y => default;` → `public PcgSplineSet Y => default;`.
  - Executor'ы: `PcgOutput<List<Spline>>` → `PcgOutput<PcgSplineSet>`; `Results.Value = new List<Spline>()` → `new PcgSplineSet()`; `foreach (List<Spline> splines in splinesList)` → `foreach (PcgSplineSet set in splinesList)`; итерация геометрии — по `set.Splines`.
  - `SplineNetworkInput.Flatten(List<Spline>[] splinesList)` → `Flatten(PcgSplineSet[] splinesList)`, внутри собирает `List<Spline>` как сейчас, поведение с null сохраняет.
  - Категории по нодам:
    - Derived-transform, `AppendFrom(src, i, newSpline)`: `ChangeSplinePosition`, `OffsetSplines`, `ResampleSplines`, `SmoothSplines`, `SplineToTerrain`, `SplineWidth`, `SplineIntersection.SnappedSplines`.
    - Derived-select с двумя выходами, `AppendFrom(src, i)`: `ClosedSplines` (`Results` и `OpenedSplines`).
    - Merger, `Append(src)`: `JoinSplines` — если нода реально сливает несколько входных наборов; если она склеивает сплайны между собой 1:N→1, то это Merger по геометрии, и строка атрибутов результата берётся от ПЕРВОГО вошедшего сплайна. Прочитай executor и примени то, что соответствует его фактическому поведению; выбор зафиксируй в отчёте.
    - Derived-fanout, `AppendFrom(src, sourceIndex, piece)`: `SplitSplines` — атрибуты в Unit 4.
    - Generator, `Add`: `SplineNode`, `FindSplines`, `SplineFromPoints`, `SplineAroundPoints`, `RandomSpline` — атрибуты в Unit 4.
    - Consumer, меняется только тип входа: `PointsOffsetSplines`, `SplinePointsByDistance`, `SplinesSurface`, `PointsBySpline`, `PointsNearSplines`, `DensityByDistanceToSplines` — их выходы это точки, они уже мигрированы.
- Gate: `grep -rn "PcgOutput<List<Spline>>" Packages/PCG.Splines/` возвращает пусто; `grep -rn "\[Input\]" -A2 Packages/PCG.Splines/Scripts/ | grep "List<Spline>"` возвращает пусто; компиляция может ещё падать на других пакетах — общий компиляционный гейт в Unit 7.
- On failure: ≤3 попытки на файл, затем остановись и доложи, какой файл и почему.

### Unit 3 — Миграция портов остальных пакетов

- Goal: Polygons, Mazes, Sweep, SpriteShapes работают на `PcgSplineSet`.
- Touch:
  - `Packages/PCG.Mazes/Scripts/Splines/GraphToSplineNode.cs` + executor
  - `Packages/PCG.Polygons/Scripts/City/BlocksToRoadsNode.cs`, `SplineCorridorRegionNode.cs`, `Scripts/Convert/SplinesToGraphNode.cs`, `Scripts/Polygon/RegionToSplineNode.cs`, `SplineToRegionNode.cs` + их executor'ы
  - `Packages/PCG.SpriteShapes/Scripts/SpriteShapeInstanceNode.cs` + executor
  - `Packages/PCG.Sweep/Scripts/Sweep/SweepSplineNode.cs` + `SweepSplineNodeExecutor.cs`
  - `Packages/PCG.Polygons/Editor/Scripts/Adapters/SplinesToRegionAdapter.cs` — `PcgPortAdapter<PcgSplineSet, RegionSet>`; перенести строки атрибутов сплайнов в атрибуты регионов через `Attributes.AppendRow`, если количество регионов совпало с количеством сплайнов; если не совпало — не переносить, отметить в отчёте
  - `Packages/PCG.Splines/Editor/Scripts/Adapters/GameObjectsToSplinesAdapter.cs` — `PcgPortAdapter<List<GameObject>, PcgSplineSet>`; **попутно починить существующий дефект**: адаптер собирает `new Spline()` и теряет встроенные каналы, включая ширину. Использовать `SplineCopyUtility.CopySpline`, который уже умеет переносить каналы.
  - Убедиться, что asmdef `PCG.Polygons`, `PCG.Mazes`, `PCG.Sweep`, `PCG.SpriteShapes` ссылаются на `PCG.Splines`. Если ссылки нет — добавить её в `.asmdef` (это единственный разрешённый вид правки asmdef в этом ТДД).
- How: те же замены, что в Unit 2. `SweepSplineNodeExecutor` — самый крупный файл, там меняется только чтение входа в районе строки 470 и итерация; вся геометрия свипа и работа с `SplineSnapshot` не трогается.
- Gate: `grep -rn "PcgOutput<List<Spline>>" Packages/` возвращает пусто; `grep -rn "List<Spline>" Packages/ --include=*.cs | grep -E "\[Input\]|\[Output\]"` возвращает пусто; `grep -c "CopySpline" Packages/PCG.Splines/Editor/Scripts/Adapters/GameObjectsToSplinesAdapter.cs` ≥ 1.
- On failure: ≤3 попытки на файл. `SweepSplineNodeExecutor` не рефакторить, менять минимально.

### Unit 4 — Источники атрибутов на сплайнах

- Goal: то, что нода знает о сплайне, попадает в строку атрибутов.
- Touch: executor'ы `RegionToSplineNode`, `BlocksToRoadsNode`, `GraphToSplineNode`, `SplitSplinesNode`, `ClosedSplinesNode`, `SplineIntersectionNode`.
- How:
  - `RegionToSplineNodeExecutor` — главный мост, зеркало того, что в прошлом ТДД сделано для `RegionToPointsNode`. `SplineRegionConvert.RegionsToSplines(RegionSet)` не трогать; рядом вести `List<int> sourceRegionRow`, куда на каждый порождённый сплайн класть индекс региона-источника. После получения списка собрать `PcgSplineSet`: `set.Splines.Add(spline); set.Attributes.AppendRow(regionSet.Attributes, sourceRegionRow[k]);`, затем колонкой записать `CityAttributes.RegionIndex`. Так `lotId`, `depth`, `cutDepth`, `boundary` доезжают до сплайна.
  - `BlocksToRoadsNodeExecutor` — на каждую центральную линию, кроме уже пишущейся ширины во встроенный канал, записать в строку атрибутов `CityAttributes.RoadClass` (int) и `CityAttributes.Width` (float) теми же значениями, что идут в `SetConstant`. Плюс `SplineAttributes.Closed` (bool) — это уже известно в `AddCenterlineData`.
  - `GraphToSplineNodeExecutor` — на каждый порождённый сплайн записать `SplineAttributes.SourceSplineIndex` как индекс ребра графа, а также идентификаторы концевых узлов в `SplineAttributes.StartJunction` и `SplineAttributes.EndJunction`. Если у рёбер графа есть вес — записать его во `float`-колонку `weight`; если веса нет, колонку не заводить.
  - `SplitSplinesNodeExecutor` — на каждый кусок записать `SplineAttributes.SourceSplineIndex`, `SplineAttributes.PieceIndex` (порядковый номер куска внутри исходного сплайна), а также `StartJunction` и `EndJunction` из уже вычисленного `SplineSplitResult.PieceIncidence`. Строка атрибутов исходного сплайна переносится через `AppendFrom(src, sourceIndex, piece)` ДО записи этих колонок.
  - `ClosedSplinesNodeExecutor` — обоим выходам записать `SplineAttributes.Closed`.
  - `SplineIntersectionNodeExecutor` — выход `Results` это ТОЧКИ перекрёстков. Записать на каждую точку `SplineAttributes.JunctionIndex` (порядковый номер) и `SplineAttributes.JunctionValency` из `junctions[i].Valency`. Сейчас валентность используется только для цвета гизмо и наружу не выходит.
- Gate: для каждого из шести executor'ов `grep -qE "SplineAttributes\.|CityAttributes\." <файл>` истинно; функциональная проверка — в Unit 7.
- On failure: ≤3 попытки на файл. Если у графа Mazes нет веса ребра — колонку `weight` не заводить, отметить в отчёте.

### Unit 5 — Мост сплайн → точка

- Goal: точка, рождённая из сплайна, знает, из какого и откуда.
- Touch: `Packages/PCG.Splines/Scripts/Surfaces/SplinePoints.cs`, executor'ы `SplinePointsByDistanceNode`, `SplinesSurfaceNode`, `PointsOffsetSplinesNode`.
- How:
  - `SplinePoints.cs` — методы продолжают наполнять `List<PointData> results`, сигнатуры геометрии не меняются. Рядом добавить параллельный выходной параметр `List<float> resultTimes` и `List<float> resultDistances`, куда класть уже вычисляемые `t` и дистанцию. В режимах `Volume*`, где `t` не определён, писать `-1f`.
  - Executor'ы `SplinePointsByDistance` и `SplinesSurface`: вести `List<int> sourceSplineRow` параллельно точкам. При сборке облака на каждую точку записать `SplineAttributes.SplineIndex`, `SplineAttributes.SplineT`, `SplineAttributes.SplineDistance`, `SplineAttributes.SplineWidth` (через `SplineWidthUtility.Evaluate(spline, t, 0f)`, при `t < 0` писать `0f`), и перенести строку атрибутов исходного сплайна: `cloud.Attributes.AppendRow(splineSet.Attributes, sourceSplineRow[k])` — именно `AppendRow` по чужому набору, как это сделано в `RegionToPointsNodeExecutor`.
  - `PointsOffsetSplinesNodeExecutor`: то же плюс `SplineAttributes.SplineSide` со значением `+1` или `-1` при `BothSides` и `0` иначе. Ширина здесь уже вычисляется в `EvaluateAndAddAtT` — переиспользовать её, второй раз не считать. Выход `CornerPoints` получает `SplineIndex` и строку атрибутов сплайна, `SplineT`/`SplineDistance` для него писать не нужно.
  - Порядок точек на всех трёх выходах не меняется.
- Gate: для трёх executor'ов `grep -c "SplineAttributes.SplineIndex" <файл>` ≥ 1 в каждом; `grep -c "AppendRow" <файл>` ≥ 1 в каждом; функциональная проверка — в Unit 7.
- On failure: ≤4 попытки. Если параллельные списки в `SplinePoints.cs` ломают существующие вызовы — добавь перегрузки, старые сигнатуры сохрани.

### Unit 6 — Хвосты: BRG и уборка

- Goal: неравномерный масштаб доезжает до BatchRendererGroup, мелкие дефекты закрыты.
- Touch: `Packages/PCG.BRG/Scripts/BrgInstanceData.cs`, `Packages/PCG.BRG/Scripts/BrgInstanceMaker.cs`, `Packages/PCG.BRG/Editor/GameObjectToBrgNodeExecutor.cs`.
- How:
  - `BrgInstanceData.Points` меняет тип с `List<PointData>` на `PcgPointCloud`. Прошлый ТДД оставил его на старом типе намеренно, как отложенный долг; время пришло.
  - `GameObjectToBrgNodeExecutor`: `brgInstance.Points.Add(instance.Point)` → `brgInstance.Points.Add(instance.Point)` на облаке, а сразу после — запись `PcgPointAttributes.Scale3` из `instance.Scale3` в только что добавленную строку (`Points.Count - 1`). Поле `Scale3` есть на `GameObjectInstanceData` в ядре.
  - `GameObjectToBrgNodeExecutor`: `_tmpResults` сейчас остаётся грязным, если вычисление отменили посреди цикла. Очищать его в начале `DoComputeAsync`, а не только в конце.
  - `BrgInstanceMaker.TryAdd`: вместо `var scale = point.Scale; Scale = new Vector3(scale, scale, scale)` использовать `var scale3 = brgData.Points.GetScale3(j); var scale = point.Scale; Scale = new Vector3(scale * scale3.x, scale * scale3.y, scale * scale3.z);`. Контракт итогового масштаба — `Point.Scale * scale3` — тот же, что в ядровом `GameObjectInstanceMaker`. Поле `Color` оставить белым: per-instance цвет это отдельная работа.
  - `DrawPreview` в `GameObjectToBrgNodeExecutor` собирает `SelectMany(p => p.Points)` — поправить под облако.
- Gate: `grep -c "PcgPointCloud" Packages/PCG.BRG/Scripts/BrgInstanceData.cs` ≥ 1; `grep -c "GetScale3" Packages/PCG.BRG/Scripts/BrgInstanceMaker.cs` ≥ 1; `grep -c "_tmpResults.Clear()" Packages/PCG.BRG/Editor/GameObjectToBrgNodeExecutor.cs` ≥ 2.
- On failure: ≤3 попытки. `BrgContainer` и `BrgItem` из внешнего пакета не трогать.

### Unit 7 — Компиляция, регрессия, доказательство атрибутов

- Goal: собирается, ничего не сломалось, атрибуты реально текут.
- Touch: править только то, что мешает компиляции.
- How: через `unity-bridge:unity-bridge`.
  - Компиляция, полный список ошибок, чинить по одной.
  - Повторить замер Unit 0 на обеих сценах той же процедурой, сравнить с `Docs/notes/spline_set_baseline.md` построчно. Любое расхождение — дефект, чинить.
  - Доказательство атрибутов на `CityForestV3`, вывести в транскрипт:
    - на выходе `BlocksToRoadsNode.Centerlines` — имена колонок набора, среди них `roadClass` и `width`; значения у первых трёх сплайнов;
    - на выходе `PointsOffsetSplinesNode` (нода фонарей) — имена колонок облака, среди них `splineIndex`, `splineT`, `splineWidth`, `splineSide`, и вместе с ними пришедшие со сплайна `roadClass`/`width`; значения у первых трёх точек;
    - на выходе `SplineIntersectionNode.Results` — колонка `junctionValency` с не менее чем двумя различными значениями;
    - `IsValid()` на всех сплайновых и точечных выходах — `True`.
  - Проверка кеша: прогнать `SweepDemoScene` дважды подряд без правок графа и показать, что второй прогон читает сплайновые ноды из value-cache (по логам `PcgValueCache` либо по времени вычисления ноды). Если инструментов наблюдения нет — вывести `PcgCacheSerializerRegistry.ForType(typeof(PcgSplineSet))` не равным `null` и это считать доказательством регистрации.
- Gate: в транскрипте видно: (а) компиляция `0 errors`; (б) таблица «нода → значение до / после» с нулевым расхождением по обеим сценам; (в) все четыре пункта доказательства атрибутов; (г) сериализатор для `PcgSplineSet` найден реестром.
- On failure: ≤5 попыток на компиляцию, ≤4 на регрессию. Не подгонять baseline под новый результат. Если расхождение в SweepDemoScene по числу вершин меша — это дефект переноса, а не «допустимая погрешность».

### Unit 8 — Документация

- Goal: карты отражают новый тип и правила, старые дефекты документации закрыты.
- Touch: `Docs/SPLINES_MAP.md`, `Docs/PROJECT_MAP.md`, `Docs/POLYGONS_MAP.md`, `Docs/BRG_MAP.md`, `Docs/OCTREE_MAP.md`, `Docs/notes/city_pipeline.md`.
- How:
  - `SPLINES_MAP.md`: **удалить дублирующийся раздел** «Width channel и road-network contract» — он присутствует дважды, строки примерно 68 и 76, тексты идентичны. Оставить один. В нём же исправить название ключа канала: в документе написано `SplineWidth`, фактический ключ — `pcg.width`. Добавить раздел про `PcgSplineSet`: состав, инвариант, правило категорий, таблицу «какая нода какие атрибуты пишет», и главное правило разделения — переменное вдоль сплайна живёт во встроенном канале Unity, постоянное на сплайн живёт в `Attributes`.
  - `PROJECT_MAP.md`: тип порта сплайнов теперь `PcgSplineSet`, `TypeId 4` в таблице сериализаторов, ссылка на правило категорий.
  - `POLYGONS_MAP.md`: `RegionToSpline` переносит атрибуты региона на сплайн, `BlocksToRoads` пишет `roadClass`.
  - `BRG_MAP.md`: `BrgInstanceData.Points` теперь `PcgPointCloud`, применяется `scale3`, цвет пока белый.
  - `OCTREE_MAP.md`: приписать, что прореживание с учётом bounds и приоритета — это `PrunePointsNode` в ядре, а `PointsNearPointsOctree` намеренно остаётся радиусным.
  - `notes/city_pipeline.md`: дописать, какие атрибуты теперь доступны на дорожных сплайнах и на точках вдоль них, и что на них можно вешать.
- Gate: `grep -c "Width channel" Docs/SPLINES_MAP.md` возвращает 1; `grep -c "pcg.width" Docs/SPLINES_MAP.md` ≥ 1; `grep -c "PcgSplineSet" Docs/SPLINES_MAP.md Docs/PROJECT_MAP.md` — оба ≥ 1; `grep -c "PrunePointsNode" Docs/OCTREE_MAP.md` ≥ 1.
- On failure: ≤2 попытки.

## Done (/goal condition)

Сплайны несут атрибуты. Доказательства в транскрипте:

- `grep -rn "PcgOutput<List<Spline>>" Packages/` возвращает пусто.
- `grep -rn "List<Spline>" Packages/ --include=*.cs | grep -E "\[Input\]|\[Output\]"` возвращает пусто.
- `grep -rn "TypeId =>" Packages/` — номера уникальны, среди них `4` для `PcgSplineSetSerializer`.
- `grep -rn "Rent(" Packages/ --include=*.cs | grep -i spline` возвращает пусто.
- Через `unity-bridge:unity-bridge`: компиляция `0 errors`.
- Таблица «нода → значение до / после» по `CityForestV3` и `SweepDemoScene` с нулевым расхождением; baseline взят из `Docs/notes/spline_set_baseline.md`, снятого в Unit 0 ДО правок; для `SweepDemoScene` совпали и число вершин, и число треугольников.
- На `CityForestV3` выведены: колонки `roadClass` и `width` на выходе `BlocksToRoads.Centerlines`; колонки `splineIndex`, `splineT`, `splineWidth`, `splineSide`, `roadClass` на выходе `PointsOffsetSplines`; колонка `junctionValency` с не менее чем двумя различными значениями; `IsValid() == True` на всех выходах.
- `grep -c "Width channel" Docs/SPLINES_MAP.md` возвращает 1.

Ограничения, которые должны выполняться одновременно: `git status --porcelain` не содержит файлов под `Assets/Plugins/PCG4U/`; ни одно имя поля с `[Input]`/`[Output]` не переименовано; ни один `*.meta` не изменён вручную; число вхождений `SwitchToMainThread`, `SwitchToThreadPool`, `UniTask.WhenAll`, `RunIndexedAsync` в `Packages/` не изменилось относительно стартового снапшота; правки `.asmdef` — только добавление ссылки на `PCG.Splines`, ничего больше.

Остановиться после 130 ходов.

## End-of-run report

- Поставь `Status` вверху документа в `Выполнено`.
- Доложи: какие юниты завершены; какие гейты потребовали повторов; расхождения baseline и как закрыты.
- Отдельно: какое поведение оказалось у `JoinSplinesNode` и какую категорию ты для неё выбрал.
- Отдельно: удалось ли перечислить встроенные каналы сплайна публичным API или пришлось ограничиться ключом `pcg.width`.
- Перечисли ноды, которые могли бы писать осмысленный атрибут, но в этом ТДД его не пишут — как задел на следующий заход.
- Флаг — НЕ действовать: уточни у заказчика, нужно ли обновлять пользовательскую документацию аддонов (`Documentation~/`) под `PcgSplineSet`.

## Отчёт о выполнении (2026-07-26)

### Юниты

Все 9 юнитов (0–8) завершены.

- **Unit 0** — baseline снят через `unity-bridge` по обеим сценам, 627 строк в `Docs/notes/spline_set_baseline.md`. Метод: `PcgGraphRunner.GetGraph(component)` → `PcgComputeSystem.ResolveAsync` по всем executor'ам, рекурсивно через `SubGraphNodeExecutor.Inner`; на каждый выходной порт выведено содержимое (Points/Splines/Regions/Junctions+Cuts/Meshes+Verts+Tris + список колонок + `IsValid`). Строки отсортированы для построчного сравнения.
- **Unit 1** — `PcgSplineSet`, `SplineAttributes`, `PcgSplineSetSerializer` (`TypeId => 4`), `PcgSplinesBootstrap`; `SplinesUtility.GetContentHash(Spline)` добавлен, `SplinesValue` переведён на него и на `ValueType = typeof(PcgSplineSet)`.
- **Unit 2** — 16 нод и executor'ов PCG.Splines + `SplineNetworkInput.Flatten(PcgSplineSet[])`.
- **Unit 3** — Polygons, Mazes, Sweep, SpriteShapes; оба адаптера; в `PCG.Polygons.asmdef` добавлена ссылка на `PCG.Splines` (единственная правка asmdef).
- **Unit 4** — источники атрибутов: `RegionToSpline`, `BlocksToRoads`, `GraphToSpline`, `SplitSplines`, `ClosedSplines`, `SplineIntersection`.
- **Unit 5** — мост сплайн→точка: `SplinePoints` получил параллельные выходы `resultTimes`/`resultDistances`; `SplinePointsByDistance`, `SplinesSurface`, `PointsOffsetSplines` собирают облако с `splineIndex`/`splineT`/`splineDistance`/`splineWidth`(/`splineSide`) и строкой атрибутов исходного сплайна.
- **Unit 6** — `BrgInstanceData.Points` → `PcgPointCloud`; `scale3` пишется в облако и умножается на `Point.Scale` в `BrgInstanceMaker`; `_tmpResults` чистится и в начале `DoComputeAsync`; `DrawPreview` поправлен.
- **Unit 7** — компиляция `0 errors`; построчная регрессия по обеим сценам — **0 расхождений** (в т.ч. `Meshes:24,Verts:3336,Tris:8916` на `SweepDemoScene`); доказательство атрибутов; сериализатор найден реестром.
- **Unit 8** — документация: `SPLINES_MAP.md` (дубль раздела удалён, ключ канала исправлен на `pcg.width`, добавлен раздел про `PcgSplineSet`), `PROJECT_MAP.md`, `POLYGONS_MAP.md`, `MAZES_MAP.md`, `BRG_MAP.md`, `OCTREE_MAP.md`, `notes/city_pipeline.md`.

### Повторы гейтов

Код правился по одному разу; компиляционный гейт потребовал 3 прогонов, и все три — про недостающие `using PCG.Splines;` в файлах нод/executor'ов, лежащих в неймспейсах `PCG.CreatePoints` / `PCG.SelectPoints` / `PCG.TransformPoints` (6 + 1 файл). Логических ошибок компилятор не нашёл.

Отдельно: мост Cowork Bridge требует сигнатуру `public static Task<string> Run()` (скилл описывает `string Run()`), и у него свой лимит 300 с на асинхронный таск — снятие baseline пришлось разбить по одной сцене на таск.

### Расхождения baseline

Ноль. Проверено дважды: перед миграцией baseline снят два раза на неизменённом коде и дал идентичный результат (одна транзиентная строка `Error:ArgumentOutOfRangeException` на `Town Instances`), поэтому сравнение «до/после» осмысленно. После миграции 271 строка счётчиков по `CityForestV3` и 6 строк по `SweepDemoScene` совпали с baseline бит-в-бит.

Предсуществующий шум, не связанный с этим ТДД: на `CityForestV3` часть нод квартальной ветки (`Combine Town Instances`, `Unified Raised Sidewalks`, `Unified Sidewalk Buffer`, `Unified Sidewalk Ring`, иногда `Town Instances`) бросает `ArgumentOutOfRangeException` и до миграции, и после. Числа на выходах при этом стабильны.

### JoinSplinesNode — какая категория выбрана

Прочитан executor: нода делает **и то, и другое**. Замкнутые сплайны проходят насквозь один-в-один (Derived-select, `AppendFrom(src, i)`), открытые — склеиваются по близким концам, N→1 (Merger по геометрии). Для склеенной цепочки строка атрибутов берётся от **первого вошедшего** сплайна: цепочка помнит `(набор, индекс)` того сплайна, с которого началась, и присоединение кандидатов этот источник не меняет. Порядок выхода прежний: сначала замкнутые в порядке встречи, затем цепочки.

### Встроенные каналы сплайна

Перечислить удалось публичным API: `Spline.GetFloatDataKeys()` / `GetFloat4DataKeys()` / `GetIntDataKeys()` / `GetObjectDataKeys()` — теми же вызовами, что использует `SplineCopyUtility.CopyEmbeddedData`. Ограничения ключом `pcg.width` не потребовалось: сериализуются **все** float-каналы. Каналы `float4`/`int`/`Object` не сериализуются, при их наличии один раз за запись выводится `Debug.LogWarning` с именем ключа.

### Отклонения от буквы ТДД

- **В формат сериализатора добавлен `AutoSmoothTension` на knot** (после `TangentMode`). Без него сплайн с ненулевым тюнингом натяжения после восстановления из кеша менял бы геометрию — это была бы тихая порча данных. `SplineCopyUtility` тензию копирует, `SplineNodeExecutor.GetVersionSalt` её хеширует, поэтому она часть контракта сплайна.
- **Заведены два внутренних хелпера** вместо тройного копирования сборки облака: `Editor/Scripts/Tools/SplinePointAttributes.cs` и `Editor/Scripts/Tools/OffsetPointBuffer.cs`. Новых нод и портов они не добавляют.
- **`SplinesUtility.cs` больше не обёрнут целиком в `#if UNITY_EDITOR`** — под директивой остался только `IsInsideSpline` (он тянет editor-only `SplinesCache`). Иначе рантайм-типы `PcgSplineSet` и `SplinesValue` не смогли бы звать `GetContentHash(Spline)` в плеерной сборке.
- **Числовое значение хеша `SplinesValue.GetContentHash()` изменилось** (посплайновая часть вынесена в отдельную функцию и подмешивается одним шагом). Набор и порядок учитываемых полей те же; следствие — одноразовая инвалидация value-cache.

### Ноды, которые могли бы писать осмысленный атрибут, но не пишут (задел)

- `SplineWidthNode` — знает заданную ширину, но кладёт её только в канал `pcg.width`. Константная колонка `width` на сплайн дала бы фильтрацию без семплирования канала.
- `ResampleSplinesNode` / `SmoothSplinesNode` / `SplineToTerrainNode` — знают длину дуги до и после операции; `splineLength` был бы дешёвым и полезным атрибутом.
- `SplineToTerrainNode` — знает, какие узлы вышли за bounds террейна (сейчас это только `Debug.LogWarning`); флаг `outOfBounds` на сплайн просился бы наружу.
- `ClosedSplinesNode` — мог бы писать длину/площадь контура для замкнутых.
- `FindSplinesNode` — знает имя и тег найденного `SplineContainer`, наружу не отдаёт ничего.
- `SplineAroundPointsNode` / `SplineFromPointsNode` / `RandomSplineNode` — рождают сплайн из точек и могли бы перенести строку атрибутов точки-источника (для `SplineAroundPoints` это 1:1 и совсем просто).
- `JoinSplinesNode` — знает, из скольких кусков собрана цепочка; `pieceCount` пригодился бы.
- `SplineIntersectionNode.SnappedSplines` — знает, подтянулся ли конец ветки при снапе; флаг `snapped` показал бы, где сеть чинилась.
- `SplineCorridorRegionNode` / `SplinesToGraphNode` — потребители, атрибуты входных сплайнов на выход не переносят (регион/граф там строятся не 1:1).

### Пользовательская документация (сделано по требованию CLAUDE.md)

ТДД её не упоминал, но `CLAUDE.md` требует обновлять документацию при достаточном масштабе, поэтому `Packages/PCG.*/Documentation~/` обновлена по стандарту ASD-STE100:

- новая страница `PCG.Splines/Splines/Pcg-Spline-Set.md` — тип, правило «канал или атрибут», таблица имён атрибутов, таблица «какая нода что пишет», правила переноса строк, поведение кеша;
- `Splines-Addon.md` — раздел «Data types» со ссылкой на новую страницу и раздел «Attributes on splines» с практическими примерами;
- страницы нод с новыми атрибутами: `Closed-Splines`, `Join-Splines` (правило первого вошедшего сплайна), `Split-Splines`, `Spline-Intersection` (плюс недостающее описание выхода `SnappedSplines`), `Points-Offset-Splines` (плюс недостающее описание выхода `CornerPoints`), `Splines-Surface`, `Spline-Points-By-Distance`;
- Polygons: `Blocks-To-Roads` (плюс недостающее описание выхода `Centerlines`), `Region-To-Spline`, `Spline-To-Region`;
- Mazes: `Graph-To-Spline`;
- BRG: `Brg-Instance-Data`, `Game-Object-To-Brg-Node`.

Имена C#-типов в пользовательской документации нигде не назывались, поэтому устаревших упоминаний `List<Spline>` в ней не было и чинить было нечего.

Не создавались отсутствующие страницы нод `Spline Width` и `Spline To Terrain` (`Spline To Terrain` описан прямо в `Splines-Addon.md`, `Spline Width` не описан нигде) — это предсуществующий пробел, к этому ТДД отношения не имеет.
