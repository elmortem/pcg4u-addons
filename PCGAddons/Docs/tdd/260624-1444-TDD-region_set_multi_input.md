Status: Выполнено

# ТДД: Мультивход RegionSet в нодах

## Контекст и задача

Все входные порты типа `RegionSet` в нодах `PCG.Polygons` помечены `PcgConnectionType.Override` и читаются через `GetInputValue` — это допускает только одну связь на порт. Чтобы подать в ноду несколько наборов, приходится сначала объединять их нодой `PolygonBoolean` (Union), что геометрически сливает перекрытия и теряет отдельные регионы.

Задача — разрешить подключать несколько `RegionSet` в один порт (как уже работают порты `List<PointData>`): связи конкатенируются в один набор с сохранением всех регионов и их перекрытий. Контракт выходов не меняется — на выходе по-прежнему один `RegionSet`.

Входы террейна (`RegionToMeshNode.Terrain`, `RegionToMeshNode.Offset`) в этой задаче не трогаются.

## Механика портов

- `PcgConnectionType.Override` рвёт прежние связи при новом подключении (`DisconnectAll` в `PcgExecGraph.Connect`) → один вход. `PcgConnectionType.Multiple` (дефолт `InputAttribute.Connection`) разрешает несколько связей.
- `GetInputValue<T>` читает только первую связь (`port.Connection` = `Connections[0]`). `GetInputValues<T>` возвращает `T[]` по всем связям.
- Перевод входа на мультивход = снять `Override` (вернуть дефолт `Multiple`) на data-ноде + читать через слияние `GetInputValues` в исполнителе.

## Слияние наборов

Стратегия — конкатенация. Новый `RegionSet`, в него добавляются клоны всех полигонов всех входных наборов; регион-атрибуты сливаются построчно через `PcgAttributeSet.Append` (он выравнивает разный состав колонок); рёберные атрибуты переносятся внутри `Polygon2D.Clone`. `PlaneY` берётся от первого подключённого набора.

Поведение по числу связей:

- 0 связей → `null` (как прежний fallback).
- 1 связь → набор возвращается напрямую, без клона (текущее поведение и скорость сохраняются).
- 2 и более → клонирование и слияние выполняются в пуле потоков (`UniTask.SwitchToThreadPool` → работа → `UniTaskEditor.SwitchToEditorThread`), чтобы не блокировать редактор.

### Метод RegionSet.Append

Файл: `Packages/PCG.Polygons/Scripts/Polygon/RegionSet.cs` — добавить метод в существующий класс.

```csharp
public void Append(RegionSet other)
{
	for (int i = 0; i < other.Regions.Count; i++)
	{
		Regions.Add(other.Regions[i].Clone());
	}

	Attributes.Append(other.Attributes);
}
```

### Хелпер RegionSetInput

Новый файл: `Packages/PCG.Polygons/Editor/Scripts/Exec/RegionSetInput.cs` (editor-сборка `PCG.Polygons.Editor`).

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PCG.Exec;
using PCG.Utilities;

namespace PCG.Polygons
{
	public static class RegionSetInput
	{
		public static async UniTask<RegionSet> ReadCombinedAsync(PcgNodeExecutor executor, string fieldName, CancellationToken ct)
		{
			var sets = executor.GetInputValues<RegionSet>(fieldName);
			if (sets == null || sets.Length <= 0)
				return null;

			var valid = new List<RegionSet>(sets.Length);
			for (int i = 0; i < sets.Length; i++)
			{
				if (sets[i] != null)
					valid.Add(sets[i]);
			}

			if (valid.Count <= 0)
				return null;

			if (valid.Count == 1)
				return valid[0];

			await UniTask.SwitchToThreadPool();

			var result = new RegionSet();
			result.PlaneY = valid[0].PlaneY;
			for (int i = 0; i < valid.Count; i++)
			{
				if (ct.IsCancellationRequested)
					break;

				result.Append(valid[i]);
			}

			await UniTaskEditor.SwitchToEditorThread();
			return result;
		}
	}
}
```

## Изменения в data-нодах

В каждом поле снять `(Connection = PcgConnectionType.Override)`, оставив `[Input]` (дефолт `Multiple`). Прочие входы и параметры нод не трогать.

- `Packages/PCG.Polygons/Scripts/City/SubdivideRegionNode.cs` — поле `Region`
- `Packages/PCG.Polygons/Scripts/City/AssignRoadClassByDepthNode.cs` — поле `Blocks`
- `Packages/PCG.Polygons/Scripts/City/BlocksToRoadsNode.cs` — поле `Blocks`
- `Packages/PCG.Polygons/Scripts/City/InsetRegionNode.cs` — поле `Region`
- `Packages/PCG.Polygons/Scripts/City/LotsFromBlockNode.cs` — поле `Blocks`
- `Packages/PCG.Polygons/Scripts/City/PolygonBooleanNode.cs` — поля `A` и `B`
- `Packages/PCG.Polygons/Scripts/City/RegionToPointsNode.cs` — поля `Region` и `Roads`
- `Packages/PCG.Polygons/Scripts/City/RegionToMeshNode.cs` — поле `Region` (поля `Terrain`, `Offset` не трогать)
- `Packages/PCG.Polygons/Scripts/SelectPoints/PointsNearRegionsNode.cs` — поле `Regions`

`RegionToSplineNode.Region` уже объявлен как `[Input]` без `Override` — ноду не трогать.

Пример замены (на `SubdivideRegionNode`):

```csharp
[Input(Connection = PcgConnectionType.Override)]
public RegionSet Region;
```

```csharp
[Input]
public RegionSet Region;
```

## Изменения в исполнителях

В каждом исполнителе заменить синхронное чтение RegionSet-входа на `await RegionSetInput.ReadCombinedAsync(this, <имя>, ct)`. Остальная логика исполнителя не меняется.

| Файл | Было | Стало |
|---|---|---|
| `Editor/Scripts/Exec/SubdivideRegionNodeExecutor.cs` | `var input = GetInputValue(nameof(Data.Region), Data.Region);` | `var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);` |
| `Editor/Scripts/Exec/AssignRoadClassByDepthNodeExecutor.cs` | `var input = GetInputValue(nameof(Data.Blocks), Data.Blocks);` | `var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Blocks), ct);` |
| `Editor/Scripts/Exec/BlocksToRoadsNodeExecutor.cs` | `var input = GetInputValue(nameof(Data.Blocks), Data.Blocks);` | `var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Blocks), ct);` |
| `Editor/Scripts/Exec/InsetRegionNodeExecutor.cs` | `var input = GetInputValue(nameof(Data.Region), Data.Region);` | `var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);` |
| `Editor/Scripts/Exec/LotsFromBlockNodeExecutor.cs` | `var input = GetInputValue(nameof(Data.Blocks), Data.Blocks);` | `var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Blocks), ct);` |
| `Editor/Scripts/Exec/PolygonBooleanNodeExecutor.cs` | `var a = GetInputValue(nameof(Data.A), Data.A);` | `var a = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.A), ct);` |
| `Editor/Scripts/Exec/PolygonBooleanNodeExecutor.cs` | `var b = GetInputValue(nameof(Data.B), Data.B);` | `var b = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.B), ct);` |
| `Editor/Scripts/Exec/RegionToPointsNodeExecutor.cs` | `var input = GetInputValue(nameof(Data.Region), Data.Region);` | `var input = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);` |
| `Editor/Scripts/Exec/RegionToPointsNodeExecutor.cs` | `var roads = GetInputValue(nameof(Data.Roads), Data.Roads);` | `var roads = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Roads), ct);` |
| `Editor/Scripts/Exec/RegionToMeshNodeExecutor.cs` | `var region = GetInputValue(nameof(Data.Region), Data.Region);` | `var region = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);` |
| `Editor/Scripts/Exec/RegionToSplineNodeExecutor.cs` | `var region = GetInputValue(nameof(Data.Region), Data.Region);` | `var region = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Region), ct);` |
| `Editor/Scripts/Exec/PointsNearRegionsNodeExecutor.cs` | `var regions = GetInputValue(nameof(Data.Regions), Data.Regions);` | `var regions = await RegionSetInput.ReadCombinedAsync(this, nameof(Data.Regions), ct);` |

`RegionToPointsNodeExecutor` сохраняет строку `var results = Results.Rent(input != null ? input.Count : 0);` сразу после чтения `input` — порядок не меняется, только источник `input`.

## Доступ к типу RegionSetInput

`RegionSetInput` объявлен в namespace `PCG.Polygons`. Дополнительные `using` не нужны:

- Исполнители города в namespace `PCG.Polygons.City` видят `PCG.Polygons` как охватывающий namespace.
- `RegionToSplineNodeExecutor` — в namespace `PCG.Polygons`.
- `PointsNearRegionsNodeExecutor` (namespace `PCG.SelectPoints`) уже содержит `using PCG.Polygons;`.

## Превью и кэш

Без изменений. Выход каждой ноды остаётся одним `RegionSet`, поэтому value-cache (`RegionSetSerializer`) и превью (`RegionGizmoUtility`) не затрагиваются.

## Затронутые файлы

Новый:

- `Packages/PCG.Polygons/Editor/Scripts/Exec/RegionSetInput.cs`

Изменяемые (рантайм):

- `Packages/PCG.Polygons/Scripts/Polygon/RegionSet.cs` (+ метод `Append`)
- 9 data-нод из раздела «Изменения в data-нодах»

Изменяемые (editor):

- 10 исполнителей из таблицы раздела «Изменения в исполнителях» (12 правок: в `PolygonBooleanNodeExecutor` и `RegionToPointsNodeExecutor` по два входа)

---

После выполнения:

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта (`Docs/PROJECT_MAP.md`) — новый тип `RegionSetInput` и метод `RegionSet.Append`, а также смена поведения RegionSet-входов с одиночного на множественный.
