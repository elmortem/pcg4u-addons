Status: Выполнено (Unit 6 — частично, см. отчёт)

# CityForest V4 — Agent Execution Spec

Цель: собрать в PCGAddons новую демку `CityForestV4` — красивую конкретную деревню в лесу на уровне демок Unreal PCG. Не переиспользуемый генератор: ручные контуры районов легальны. V2 и V3 не трогаем — V4 строится рядом для сравнения before/after.

## References (not inlined)

- Конвенции кода и проекта: `CLAUDE.md` (корень PCGAddons). Табы, типы в отдельных файлах, без комментариев, тесты для новых нод, meta-файлы не править руками.
- Карты проекта: `Docs/PROJECT_MAP.md`, `Docs/POLYGONS_MAP.md`, `Docs/SPLINES_MAP.md` — сверяться перед правками, обновить в конце.
- Рецепт city-пайплайна: `Docs/notes/city_pipeline.md`.
- Базовые счётчики точек V3: `Docs/notes/point_cloud_migration_baseline.md`.
- Документация нод ядра: `Assets/Plugins/PCG4U/Documentation/PCG/*.md` (117 файлов; в т.ч. Game Objects Assembly, Assembly Capture, Poisson Points, Points By Attribute, Game Objects By Attribute).
- Sweep-перекрёстки: серия закрытых ТДД `Docs/tdd/done/2607*` (junctions, plates, corner separation, topology normalize, full profile mesher) + доки `Packages/PCG.Sweep`.
- Работа с графом: **PCG4U MCP** (конфиг `.mcp.json` в корне проекта) — чтение структуры графов, создание/удаление нод и связей, установка параметров, счётчики точек по нодам.
- Работа в редакторе: skill `unity-bridge:unity-bridge` (agentbridge CLI) — выполнение C# в Editor, запуск тестов, генерация, скриншоты, аудиты сцены.
- Поиск бесплатных ассетов: WebSearch/WebFetch. Только CC0 (предпочтительно) или CC-BY с фиксацией атрибуции.

## Foundations (shared, used across units)

Пути-источники (только чтение):

- Сцена-донор: `Assets/Examples/CityForestV3/CityForestV3.unity`.
- Графы-доноры: `Assets/Examples/CityForestV3/Graphs/CityForestTown.asset` (261 нода: 8 районов BSP, дороги, растительность, сабграф FenceAlongSpline), `Assets/Examples/CityForestV3/Graphs/ForestV3.asset`.
- Террейн-донор: `Assets/Examples/CityForestV3/Terrain/CityForestTerrain.asset`.
- Маска леса: `Assets/Plugins/PCG4U/Examples/Masks/ForestMask.asset` (использовать по ссылке, не копировать, не править).
- Префабы домов: `Assets/Examples/CityForestV3/Prefabs/KenneyTown/` (9 шт), природа: `.../KenneyNature/`.
- Паки: `Assets/ThirdParty/Kenney*` (CC0, формат нотиса — `SOURCE.md` в папке пака: Source URL, Imported content, License + `License.txt`).

Целевая структура (всё новое демо-содержимое только здесь):

- `Assets/Examples/CityForestV4/` — `CityForestV4.unity`, `Graphs/CityForestTownV4.asset`, `Graphs/ForestV4.asset`, `Terrain/CityForestTerrainV4.asset`, `Prefabs/`, `Assemblies/`, `Screenshots/`, `README.md`.

Новый код нод — только в `Packages/PCG.Polygons` и `Packages/PCG.Octree` (+ их тестовые asmdef; расположение тестов — по образцу существующих тестов в этих пакетах; если тестов в пакете нет — создать `Tests/Editor` с asmdef по образцу `PCG.Authoring.Tests`).

Факты о V3, на которые опираются юниты:

- Классы дорог: `AssignRoadClassByDepth`, WidthByDepth 1 / 0.82 / 0.52 / 0.32 при MaxWidth 5.5 → фактические ширины ≈ 5.5 / 4.51 / 2.86 / 1.76. Классификация по ширине: ≥5 → class 0, ≥4 → class 1, ≥2.5 → class 2, иначе class 3.
- Известные дефекты донора, которые НЕ переносим в V4: самоссылка `CombineInstances.Results → CombineInstances.Instances`; неподключённые цепочки «Central Towers» (H=58) и «Central Landmark» (H=78); legacy-объекты `City` и `Path` в сцене; рассинхрон ширины артериалов (SplineWidth=8 против SplineCorridorRegion Width=6); дома по центроидам лотов; сетка Grid+Jitter для травы; бинарный вырез леса без опушки; захардкоженные сиды в 20+ местах.
- Точные имена нод, портов и параметров перед каждой правкой графа сверять через PCG4U MCP и документацию нод — не угадывать по памяти.

## Invariants (must hold throughout)

- `git status` не показывает изменений в: `Assets/Examples/CityForest/` (V2), `Assets/Examples/CityForestV3/`, `Assets/Plugins/PCG4U/`, существующих папках `Assets/ThirdParty/Kenney*`.
- Террейн не деформируется кодом и нодами: никаких новых нод изменения heightmap/splatmap, никакого `TerrainData.SetHeights` в скриптах генерации. Выравнивание террейна под дорогами — ручная работа заказчика после прогона (отметить места в отчёте).
- Публичные API существующих нод ядра и аддонов не меняются.
- Новые файлы кода — только в `Packages/PCG.Polygons` и `Packages/PCG.Octree`; новые ассеты — только в `Assets/Examples/CityForestV4/` и новых папках `Assets/ThirdParty/<НовыйПак>/`.
- `*.meta` руками не создавать и не править — только через Unity (bridge → `AssetDatabase.Refresh`).
- Каждый новый ассет из интернета: лицензия CC0 или CC-BY, папка `Assets/ThirdParty/<Пак>/` с `SOURCE.md` и `License.txt` по существующему формату.

## Execution Plan

Юниты идут в указанном порядке, кроме помеченных [parallel].

### Unit 0 — Preflight

- Goal: подтверждена работоспособность обоих каналов управления — PCG4U MCP и unity-bridge.
- How: через MCP получить список графов и структуру `CityForestTown.asset` (ожидается ~261 нода — число фиксируется как базовое); через bridge выполнить тривиальный скрипт (вернуть `Application.unityVersion`).
- Gate: в транскрипте — список нод графа от MCP и версия Unity от bridge.
- On failure: любой из каналов недоступен → остановиться и доложить. Не изобретать обходов, не читать YAML руками.

### Unit 1 — Каркас V4 и гигиена

- Goal: существует рабочая копия демки в `Assets/Examples/CityForestV4/`, генерирующаяся без ошибок и без унаследованных дефектов донора.
- Touch: вся папка `Assets/Examples/CityForestV4/` (создать); копии через bridge `AssetDatabase.CopyAsset`: сцена, оба графа (→ `CityForestTownV4.asset`, `ForestV4.asset`), TerrainData (→ `CityForestTerrainV4.asset`).
- How:
  - Скопировать ассеты, открыть `CityForestV4.unity`, перевязать: Terrain-компонент → V4 TerrainData; все `PcgComponent` → V4-графы; `District City V3` переименовать в `District City V4`.
  - Удалить из сцены legacy-объекты `City` (пресет CityBlocks) и `Path` (StonePath).
  - В `CityForestTownV4.asset` через MCP: удалить самоссылку `CombineInstances.Results → CombineInstances.Instances`; удалить цепочки «Central Towers» и «Central Landmark» целиком (Spline → SplineToRegion → RoundRegion → RegionExtrude, ноды ~124–131) — их заменит Unit 7.
  - Прогнать полную генерацию через bridge.
- Gate: генерация завершается без ошибок в консоли; MCP подтверждает отсутствие самоссылки и удалённых цепочек; в иерархии сцены нет объектов `City` и `Path`; `git status` — изменений вне разрешённых путей нет.
- On failure: ≤3 попыток на шаг; если копия сцены не перевязывается — остановиться и доложить.

### Unit 2 — Нода LotFrontagePoints [parallel]

- Goal: в `PCG.Polygons` есть нода, дающая по одной точке на лот на фронтальном ребре с отступом и ориентацией на улицу; тесты зелёные.
- Touch: `Packages/PCG.Polygons/Scripts/City/LotFrontagePointsNode.cs` (+ отдельные файлы типов по конвенции), тесты `LotFrontagePointsTests.cs`.
- How:
  - Входы: `Lots` (RegionSet — выход `LotsFromBlock` через InsetRegion), `Roads` (RegionSet — объединённый дорожный футпринт). Параметры: `Setback` (float, def 4), `MaxRoadDistance` (float, def 7), `MinFrontage` (float, def 6), `Seed` (int, для джиттера `SetbackJitter` def 0.5).
  - Алгоритм на лот: для каждого ребра полигона лота — расстояние от середины ребра до ближайшей точки границы `Roads`; фронтальное ребро = ребро с минимальным расстоянием; при разнице лучших ≤0.5 — более длинное. Если min расстояние > `MaxRoadDistance` или длина фронтального ребра < `MinFrontage` — лот пропустить. Точка = середина фронтального ребра, смещённая внутрь лота по нормали на `Setback ± jitter`. Поворот Y — наружу, к дороге (нормаль фронтального ребра, направленная в сторону `Roads`).
  - Атрибуты точки: `lotId` (int), `lotArea` (float), `lotWidth` (float — длина фронтального ребра), `roadClass` (int — по ширине ближайшего дорожного коридора; ширину оценить как удвоенное расстояние от осевой до края региона в точке проекции, классификация по порогам из Foundations; если оценка ненадёжна — по расстоянию до ребра `Roads` не классифицировать, а писать class 2).
  - Реализация в стиле соседних нод `Scripts/City/` (RegionToPoints как образец структуры входов/выходов и работы с `PcgAttributeSet`).
- Gate: bridge-запуск тестов, фильтр `LotFrontagePoints` — зелёные ≥5 кейсов: квадратный лот у дороги (позиция/поворот/атрибуты), угловой лот (одно фронтальное ребро, длиннейшее из двух равных), лот без доступа к дороге (пропущен), узкий фронт (< MinFrontage, пропущен), setback с джиттером в границах.
- On failure: ≤3 попыток починки теста, затем остановиться и доложить юнит блокированным; юниты 5–6 зависят от него.

### Unit 3 — Нода PruneOverlappingPoints [parallel]

- Goal: в `PCG.Octree` есть нода взаимного разрешения пересечений между слоями инстансов; тесты зелёные.
- Touch: `Packages/PCG.Octree/Scripts/PruneOverlappingPointsNode.cs` (+ файлы типов), тесты `PruneOverlappingPointsTests.cs`.
- How:
  - Фиксированные 4 пары портов: входы `In0..In3` (PointSet), выходы `Out0..Out3`. Приоритет = индекс порта (0 сильнейший: In0 никогда не прунится об остальных, только внутри себя при `SelfPrune=true` — параметр per-port, def false для In0, true для остальных).
  - Параметры per-port: `Radius0..Radius3` (float) — базовый радиус, эффективный = Radius × max(scale.x, scale.z) точки. Общий: `Overlap` (float, def 0.9) — пара конфликтует, если дистанция XZ < Overlap × (r1 + r2).
  - Алгоритм: собрать точки всех входов; сортировка (приоритет asc, затем эффективный радиус desc); точка принимается, если не конфликтует ни с одной принятой (октри-запрос, использовать существующую инфраструктуру `PointsNearPointsOctree`); выходы — принятые точки, разложенные по исходным портам с сохранением атрибутов.
- Gate: bridge-запуск тестов, фильтр `PruneOverlappingPoints` — зелёные ≥4 кейса: пересечение между слоями (слабый удалён, сильный цел), непересекающиеся (все целы), самопересечение внутри слоя при SelfPrune, сохранение атрибутов и раскладки по портам.
- On failure: как Unit 2.

### Unit 4 — Дороги: гибрид BSP-планировки и Sweep-полотна

- Goal: в `CityForestTownV4` дороги — единая Sweep-сеть с профилем, перекрёстками и разметкой поверх BSP-планировки V3; ширины согласованы во всём графе.
- Touch: `CityForestTownV4.asset` через MCP.
- How:
  - Перед правками прочитать доки Sweep по сетевому режиму и junction plates (References) и структуру нод `SweepSpline`/`SplineIntersection` через MCP.
  - Собрать в один поток: `Centerlines` всех 8 `BlocksToRoads` + артериальные snapped-сплайны из блока внешних дорог. Ширины centerlines должны прийти из `AssignRoadClassByDepth` (проверить через MCP, что width записан на сплайнах; если нет — назначить `SplineWidth` по классам из Foundations).
  - Единый `SplineIntersection` по объединённому потоку (Tolerance 0.08, Merge 0.8, MaxHeightDiff 2, EndpointSnap 5) → `SplineToTerrain` (HeightOffset 0.05, Align, Resample, Step 1.25) → `SweepSpline` в сетевом режиме: Ribbon, `UseSplineWidth`, Height 0.18, Sides 16, Step 0.75, MaxStep 4, MaxAngle 4, junction plates включены, Cap для тупиков — Round.
  - Разметка: от артериальных snapped-сплайнов (только они) → `SplineWidth(0.24)` → второй `SweepSpline` (Width 0.24, Height 0.025), материал разметки из V2-донора (`Road Network V2` в `Assets/Examples/CityForest/CityForestV2.unity` — только чтение).
  - Ширины: артериалы `SplineWidth = 7` и `SplineCorridorRegion Width = 7` (единая переменная графа `ArterialWidth`). Придорожный декор (фонари/урны/скамейки/машины) оставить на `UseSplineWidth × 0.5` — теперь согласован.
  - `RegionExtrude "Unified Volumetric Roads"` удалить (полотно теперь Sweep); `UnionRegions "Unified Road Footprint"` оставить — он источник exclusion и тротуарного кольца; тротуары (inset +1.05 → difference → extrude 0.25) оставить как в V3.
- Gate: генерация без ошибок; MCP: значение ширины артериалов и ширины коридора читаются из одной переменной; wip-скриншот перекрёстка валентности ≥3 (`Screenshots/wip_junction.png`, механика скриншотов — как в Unit 11) прочитан агентом: полотно непрерывно, дыр и лепестков на стыке нет, разметка видна на артериалах.
- On failure: если сетевой Sweep по centerlines падает или даёт дыры — ≤3 итерации подбора Tolerance/Merge/EndpointSnap; не получилось — остановить юнит, доложить с wip-скриншотом, оставить extrude-дороги V3 как временные (не удалять `RegionExtrude` до успешного Sweep-полотна), продолжить с Unit 5.

### Unit 5 — Дома по красной линии

- Goal: каждый дом стоит на фронтальной линии своего лота с отступом, фасадом к улице; выбор префаба зависит от класса дороги и ширины лота; единый сид на весь город.
- Touch: `CityForestTownV4.asset` через MCP; префабы не менять.
- How:
  - Во всех 8 районах заменить `RegionToPoints(Centroid)` на `LotFrontagePoints` (Lots ← выход InsetRegion(-1.4) лотов, Roads ← Unified Road Footprint, Setback: районы 1–4 → 4.0, районы 5–8 → 3.5) → `PointToTerrain` (как было).
  - `LotWidth` в `LotsFromBlock` всех районов уменьшить до 14–16 (район 1,5 → 14; 2,4,7 → 15; 3,6,8 → 16) — плотнее застройка.
  - Палитры: через bridge измерить XZ-габариты 9 префабов `KenneyTown` (bounds рендереров с учётом сцены-масштаба, как в V3-инстансах); разбить на A (3 крупнейших), B (3 средних), C (3 меньших). В графе: поток точек → `PointsByAttribute` на три ветки: roadClass ≤1 И lotWidth ≥12 → `GameObjectWeights` палитра A; roadClass 2 ИЛИ lotWidth в [9,12) → палитра B; остальное → палитра C. Если точная комбинация условий двумя нодами не выражается — упростить до ветвления только по roadClass (задокументировать в отчёте).
  - Сиды: завести переменные графа `Town Seed` (int, вывести в публичные переменные компонента `District City V4`) и через целочисленные Operations-ноды (`Add Int`, `Multiply Int`) раздать производные сиды: район N → `TownSeed + N×1000`, слои декора → `TownSeed + 100+i`. Все захардкоженные сиды `2026xxxx`/`260xxx` заменить на производные.
- Gate: MCP-счётчик: суммарно домов ≥60; bridge-аудит: для каждого дома — расстояние XZ до ближайшей границы Unified Road Footprint в диапазоне [2, 9] И косинус угла между forward дома и направлением на ближайшую точку дороги > 0.8; аудит выводит `PASS houses=<n> violations=0`; смена `Town Seed` (прогнать два значения) меняет расстановку домов, но не планировку районов.
- On failure: ≤3 попыток; отдельные лоты без доступа к дороге — норма (пропуски), нарушение гейта только при violations>0 или домов <60.

### Unit 6 — Дворовые ассембли

- Goal: у ≥80% домов — двор как согласованный кластер (дорожка к улице, забор по бокам, кусты у крыльца, дерево, мелкий декор), проштампованный ассемблями, без объектов на дороге.
- Touch: `Assets/Examples/CityForestV4/Assemblies/YardAssemblies.unity` (новая staging-сцена), `CityForestTownV4.asset`.
- How:
  - Прочитать доки `Assembly Capture` и `Game Objects Assembly` в `Assets/Plugins/PCG4U/Documentation/PCG/` — точный workflow захвата и штамповки взять оттуда, не изобретать.
  - В staging-сцене собрать через bridge 3 варианта двора относительно origin = точка дома, фасад вдоль +Z: (а) «дорожка»: 4–6 камней StonePath-набора полосой от фасада к улице (z от 1 до Setback), 2 куста Nature по бокам крыльца (±2, 1), почтовый ящик RetroUrban (2.2, Setback−0.5); (б) «сад»: то же + 1 дерево Nature в углу (−4.5, −3), 3 куста по боковой границе; (в) «минимал»: дорожка + 1 куст + планter. Масштабы объектов — как в соответствующих слоях V3.
  - Захватить три ассембли, в графе штамповать на точки домов (после `PointToTerrain`): выбор варианта — взвешенно по `Seed` (а 0.45 / б 0.35 / в 0.2), поворот и позиция наследуются от точки дома.
  - Забор: переиспользовать сабграф `FenceAlongSpline` — вдоль боковых рёбер лота не строить (сложно без новой ноды); вместо этого забор остаётся только у площади (как в доноре). Кусты/деревья двора V3 (`AroundPoints` 228/231) — удалить, их заменяют ассембли.
  - Все элементы ассемблей прогнать через `PruneOverlappingPoints` в Unit 10.
- Gate: bridge-аудит: количество домов с ≥3 объектами ассембли в радиусе 8 м / общее число домов ≥ 0.8; объектов ассемблей внутри Unified Road Footprint = 0 (`PASS yards=<n>/<total> on_road=0`); wip-скриншот `wip_yard.png` прочитан: дорожка ведёт от двери к улице, а не в сторону.
- On failure: если механизм Assembly Capture не работает как в доках — ≤2 попытки, затем fallback: реализовать дворы как `CopyPointsToPoints` с тремя наборами смещений-детей от точки дома (та же геометрия раскладки), доложить о fallback.

### Unit 7 — Центр деревни и сторонние ассеты

- Goal: центральная площадь — композиционный якорь: заметное общественное здание, ансамбль декора, дорожки; все новые паки оформлены нотисами.
- Touch: `CityForestTownV4.asset`; `Assets/ThirdParty/<НовыйПак>/`; `Assets/Examples/CityForestV4/Prefabs/`.
- How:
  - Поиск ассета-якоря: WebSearch «kenney church CC0», «kenney town hall kit», «quaternius buildings church CC0». Критерии: low-poly flat-shaded в стиле Kenney, CC0 (или CC-BY с записью атрибуции), форматы FBX/glTF. Кандидаты в порядке предпочтения: другие киты Kenney (kenney.nl), паки Quaternius (quaternius.com). Скачать официальный архив, импортировать модели в `Assets/ThirdParty/<Пак>/Models/`, создать `SOURCE.md` + `License.txt` по формату существующих паков; нормализованный префаб-обёртку — в `Assets/Examples/CityForestV4/Prefabs/`.
  - Разместить якорь на оси площади (точка через `RegionToPoints(Centroid)` региона площади, фасадом к главному входу площади — к ближайшей артериальной дороге, поворот через `LookAtPoints` или фиксированным углом).
  - Ансамбль вокруг: кольцо скамеек и планteров (8–12 шт, `AroundPoints` по радиусу 12–16 от якоря, LookAt на якорь), 2–4 фонаря RetroUrban, радиальные дорожки StonePath от якоря к краям площади (2–3 сплайна), существующий `FenceAlongSpline` по периметру остаётся.
  - Ручной запечённый дрессинг донора («Village Square *», 45 точек) — удалить из графа, его заменяет новый ансамбль.
- Gate: `SOURCE.md` и `License.txt` существуют для каждого нового пака (вывести `cat` в транскрипт); bridge-счётчик: внутри региона площади ≥25 инстансов и ровно 1 якорь; wip-скриншот `wip_plaza.png` прочитан: якорь доминирует, декор ориентирован на него.
- On failure: скачивание недоступно (сеть/лицензия сомнительна) → fallback без интернета: якорь = самое крупное здание `KenneyTown`, масштаб ×1.4, на плинтусе `RegionExtrude` H=0.6 по центру площади; доложить. Сомнительная лицензия = не CC0/CC-BY → не использовать.

### Unit 8 — Городская растительность

- Goal: трава и деревья города распределены blue-noise с кластерами, а не сеткой; улицы озеленены.
- Touch: `CityForestTownV4.asset`.
- How:
  - «Dense Quarter Ground Cover» и «Park Grass»: `RegionToPoints(Grid)` → `RegionToPoints(Random, Count = 3×целевого)` → `DensityByNoise` (Perlin, Scale 18) → `PointsByDensity(From 0.35)` → `PoissonPoints(MinDistance 1.1)` → `DensityToScale` (как в лесу V3) → далее существующая цепочка. ExclusionRegions: дороги ∪ тротуарное кольцо ∪ площадь.
  - Уличные деревья (новый слой): артериальные snapped-сплайны → `PointsOffsetSplines` (Offset = ArterialWidth×0.5 + 1.6, Distance 16, BothSides) → `PointToTerrain` → палитра из 2–3 крупных деревьев Nature, масштаб 0.9–1.2 → в общий `CombineInstances`.
  - «Park Canopy/Understory» — оставить, но добавить `DensityByNoise`+`PointsByDensity` перед постановкой (кластеры вместо равномерного Random).
- Gate: MCP-счётчики: quarter+park grass суммарно в [2500, 6000]; уличных деревьев ≥40; wip-скриншот `wip_green.png` прочитан: регулярной сетки в траве не видно, у травы есть пятна-разрежения.
- On failure: ≤3 итерации подбора Density/Poisson; не сошлось по счётчикам — скорректировать Count источника, затем доложить.

### Unit 9 — Опушка: переход город↔лес

- Goal: лес не обрывается по линии — плотность деревьев спадает к деревне через рваную кромку, в переходной полосе кусты и молодняк.
- Touch: `ForestV4.asset`, при необходимости — значения переменных лесных компонентов в сцене V4.
- How:
  - Поток Trees: после `PointsBySpline(Outside)` добавить `DensityByDistanceToSplines` (сплайны ← Town Boundary, ремап: дистанция 12 → density 0, 30 → 1) — модуляция поверх существующего Fbm-шума (шум остаётся, край получается рваным) → существующий `PointsByDensity`.
  - Новый поток Edge (в том же графе, свой выход): кандидаты `TerrainSurface` → `PointsNearSplines` (Town Boundary, Distance ≤14, внутрь лесной стороны) → Fbm-шум → `PointsByDensity(0.4)` → `PoissonPoints(2.0)` → палитра: кусты + мелкие деревья Nature, масштаб 0.5–0.85 → выход «Edge».
  - В сцене V4 подключить выход Edge к соответствующему инстансеру (по образцу трёх существующих лесных компонентов, сид от `Forest Seed`).
  - `StabilizeTerrainPoints` сохранить во всех потоках.
- Gate: MCP-счётчики: Edge-полоса ≥300 точек; bridge-аудит: деревьев основного слоя ближе 10 м к Town Boundary = 0 (`PASS edge=<n> trees_near=0`); wip-скриншот `wip_forestedge.png` прочитан: кромка рваная, виден градиент высоты растительности.
- On failure: ≤3 итерации параметров; далее доложить.

### Unit 10 — Глобальный прунинг и согласование слоёв

- Goal: ни один инстанс не пересекается с более приоритетным; все скаттер-слои исключают дороги, тротуары и площадь; вся демка управляется двумя сидами.
- Touch: `CityForestTownV4.asset`, `ForestV4.asset`, компоненты сцены V4.
- How:
  - `PruneOverlappingPoints` в town-графе перед финальными `GameObjectWeights`: In0 = дома + якорь площади (Radius 5, SelfPrune off), In1 = все деревья города (уличные, парк, дворы; Radius 1.6), In2 = кусты (Radius 0.9), In3 = ground cover (Radius 0.35).
  - Проверить, что каждый `RegionToPoints`/скаттер-слой имеет ExclusionRegions = дороги ∪ тротуары ∪ площадь (перечислить по MCP, дополнить недостающие).
  - Убедиться: публичные переменные компонентов V4 — `Town Seed` и `Forest Seed`; других захардкоженных сидов в графах нет (MCP-обход значений Seed-параметров: все — производные от переменных).
- Gate: bridge-аудит пересечений: по всем парам (дом, дерево/куст) и (дерево, дерево) дистанция XZ ≥ 0.7×(r1+r2), вывод `PASS pairs_checked=<n> collisions=0`; суммарно инстансов ≤45000; MCP-обход сидов — «hardcoded seeds: 0».
- On failure: коллизии >0 → найти слой-источник, проверить его подключение к прунингу, ≤3 итерации; далее доложить с координатами первых 10 коллизий.

### Unit 11 — Скриншоты, чек-лист, README

- Goal: серия финальных скриншотов с фиксированных ракурсов, заполненный чек-лист качества, README демки, обновлённые карты проекта.
- Touch: `Assets/Examples/CityForestV4/Screenshots/`, `README.md`, `Docs/PROJECT_MAP.md`, `Docs/POLYGONS_MAP.md`.
- How:
  - Через bridge вычислить bounds всех town-инстансов (центр C, размер S). Отрендерить 1920×1080 PNG (утилита SceneViewShot из проекта, если пригодна, иначе временная камера + RenderTexture + `EncodeToPNG`): `Overview` (позиция C + (0, 1.2×S.max, −0.9×S.max), смотрит на C), `Hero` (высота 12 м, азимут 35°, дистанция 0.5×S.max, смотрит на площадь), `Street` (1.7 м над артериальной осевой, взгляд вдоль неё), `Junction` (25 м над перекрёстком валентности ≥3, наклон 55°), `Plaza` (15 м, смотрит на якорь), `ForestEdge` (20 м над Town Boundary, вдоль кромки).
  - Прочитать каждый PNG и заполнить чек-лист (да/нет + комментарий): линия фасадов читается вдоль улиц; полотно дорог непрерывно, перекрёстки без дыр; разметка видна; дома не пересекаются с растительностью; дворы выглядят обжитыми (дорожка/кусты/декор); площадь имеет якорь и ансамбль; трава без регулярной сетки; опушка градиентная, кромка рваная; ничего не «парит» над террейном и не утоплено.
  - `README.md` V4: что генерируется, публичные переменные (`Town Seed`, `Forest Seed`, палитры), отличия от V3, кредиты новых паков. Обновить карты проекта: новые ноды (`LotFrontagePoints`, `PruneOverlappingPoints`) и демка V4.
- Gate: 6 PNG существуют (`ls` в транскрипте); чек-лист целиком в финальном отчёте; README и карты обновлены.
- On failure: пункт чек-листа «нет» — не чинить молча: если причина локализуется в параметре одного из юнитов — одна итерация правки и повторный скриншот; иначе зафиксировать «нет» с причиной в отчёте.

## Done (/goal condition)

Демка CityForestV4 собрана и проверена: bridge-запуски тестов `LotFrontagePoints` и `PruneOverlappingPoints` зелёные (вывод в транскрипте); полная генерация сцены `Assets/Examples/CityForestV4/CityForestV4.unity` завершается без ошибок консоли; аудиты выводят `PASS houses=<n≥60> violations=0`, `PASS yards≥80% on_road=0`, `PASS edge=<n≥300> trees_near=0`, `PASS pairs_checked=<n> collisions=0`; MCP-обход сидов даёт «hardcoded seeds: 0»; 6 файлов `Assets/Examples/CityForestV4/Screenshots/{Overview,Hero,Street,Junction,Plaza,ForestEdge}.png` существуют и чек-лист из Unit 11 приведён в отчёте. Инварианты: `git status` не содержит изменений в `Assets/Examples/CityForest/`, `Assets/Examples/CityForestV3/`, `Assets/Plugins/PCG4U/`, существующих `Assets/ThirdParty/Kenney*`; каждый новый пак в `Assets/ThirdParty/` имеет `SOURCE.md` и `License.txt`. Блокированные юниты (если есть) явно перечислены в отчёте с причиной. Остановиться после 300 ходов.

## End-of-run report (the agent does this when the goal is met or it stops)

- Установить Status вверху документа в `Выполнено`.
- Отчитаться: какие юниты закрыты, какие гейты потребовали повторов, на чём остановился и почему; полный чек-лист Unit 11; список мест, где террейн нужно вручную заровнять под дорогами (координаты/скриншоты); применённые fallback'и (Sweep, ассембли, якорь площади); скачанные паки с лицензиями.
- Flag — do NOT act: уточни у заказчика, нужно ли обновлять проектную документацию сверх карт, тронутых в Unit 11.
