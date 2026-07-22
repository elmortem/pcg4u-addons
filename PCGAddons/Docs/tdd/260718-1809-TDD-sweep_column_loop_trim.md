# ТДД: Sweep — трим складок по самопересечению колонок

Status: Выполнено

Ложится поверх выполненного `260718-1756-TDD-sweep_lateral_trim.md` и заменяет боковой трим по плоскостям целиком. Плоскость чужого кольца бесконечна, а его лента конечна — ограничение полупространством режет луч там, где ленты физически нет: возле вершины шпильки кромки перетримливаются и вдоль биссектрисы остаётся щель. Верный примитив — самопересечение собственной офсетной ломаной колонки: каждая колонка профиля образует вдоль сплайна ломаную, складка — это петля этой ломаной, петля удаляется снапом внутренних колец в точку пересечения. Точка снапа лежит на обеих кромках петли по построению, поэтому ни щели, ни нахлёста на шве не бывает: кромки плеч сходятся в точке пересечения и продолжаются швом веера.

Алгоритм проверен на численном прототипе шпильки (узкий V, радиус меньше полуширины): колонки со складкой снапаются на биссектрису с точностью 1e-15, колонки без складки не двигаются, остаточных пересечений сегментов и разрывов кромки нет.

Граница метода: перекрытие поверхностей *разных* колонок внутри объединения (два плеча лежат друг на друге вдали от складки) не устраняется — это не силуэт, на матовом материале не видно, полное устранение возможно только булевым объединением в junction-ТДД. Пересечения ломаной с накопленным поворотом больше лимита (перекрёсток P-образного сплайна) сознательно не тримятся — это тоже junction-территория.

---

## Файлы

Изменяются:

- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepMeshBuilder.cs` — фаза сырых вершин, `TrimRings` заменяется на `TrimColumns`.
- `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — описание поведения трима.

Удаляются: `TrimRings`, `ExpandWindow`, константы `ClipEpsilon`, `DirEpsilon`, `ChordSafety`, массив `lateralOffsets`.

---

## Фаза сырых вершин

Массив `lateralOffsets` удаляется. `verticalOffsets` возвращается к виду до бокового трима: создаётся и заполняется только при террейне (нужен фазе драпировки). Формулы позиций не меняются. Вызов фазы трима:

```csharp
TrimColumns(frames, ups, positions, vpr, splineClosed, snapshot.MaxLateralExtent, ct, reportProgress);
```

---

## Трим колонок

Колонка `j` — ломаная из позиций вершины `j` всех колец. Работа в 2D-проекции на плоскость, ортогональную усреднённому up сплайна. Сегменты ломаной кладутся в пространственный хеш; для каждого сегмента `s` по возрастанию ищется ближайший вперёд сегмент `k`, пересекающий его в проекции. Защиты: накопленный поворот тангенсов между кольцами `s` и `k` не больше `TurnLimit` (270 градусов — отсекает перекрёстки самопересечения сплайна), расстояние между 3D-точками пересечения на двух сегментах не больше `BridgeTolerance` (отсекает мост над собой). Найденная петля удаляется: кольца `s+1..k` снапаются в точку `I3` — середину между 3D-точками пересечения, лежащую на обоих сегментах; сканирование продолжается с `k`. Снап не порождает новых пересечений: новые рёбра — под-отрезки исходных.

Для closed-сплайна алгоритм прогоняется дважды: второй прогон с виртуальным сдвигом начала на половину колец (модульная адресация) — он ловит складки, пересекающие шов. После всех колонок позиции шва копируются из кольца 0, как раньше.

`TrimRings` и `ExpandWindow` заменяются целиком:

```csharp
private const float TurnLimit = 4.712389f;
private const float BridgeToleranceFactor = 0.5f;
private const float ParallelEpsilon = 1e-12f;
private const float ParamSlack = 1e-6f;
private const float MinCellSize = 1e-3f;

private static void TrimColumns(SweepFrame[] frames, float3[] ups, float3[] positions, int vpr, bool closed, float lateralExtent, CancellationToken ct, Action reportProgress)
{
	int ringCount = frames.Length;
	int cycleCount = closed ? ringCount - 1 : ringCount;
	if (cycleCount < 3)
		return;

	var normals = new float3[cycleCount];
	for (int i = 0; i < cycleCount; i++)
		normals[i] = math.normalizesafe(frames[i].Tangent, new float3(0f, 0f, 1f));

	float3 axis = float3.zero;
	for (int i = 0; i < cycleCount; i++)
		axis += ups[i];
	axis = math.normalizesafe(axis, new float3(0f, 1f, 0f));
	float3 helper = math.abs(axis.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
	float3 e1 = math.normalize(math.cross(axis, helper));
	float3 e2 = math.cross(axis, e1);

	int segCount = closed ? cycleCount : cycleCount - 1;
	var turnAt = new float[cycleCount + 1];
	for (int i = 1; i <= cycleCount; i++)
	{
		int a = (i - 1) % cycleCount;
		int b = i % cycleCount;
		turnAt[i] = turnAt[i - 1] + math.acos(math.clamp(math.dot(normals[a], normals[b]), -1f, 1f));
	}

	float bridgeTolerance = lateralExtent * BridgeToleranceFactor;
	int progressCounter = 0;

	var projected = new float2[cycleCount];
	var cells = new Dictionary<long, List<int>>();
	var candidates = new List<int>();

	int runCount = closed ? 2 : 1;
	int shift = closed ? cycleCount / 2 : 0;

	for (int j = 0; j < vpr; j++)
	{
		for (int run = 0; run < runCount; run++)
		{
			int origin = run == 0 ? 0 : shift;

			for (int i = 0; i < cycleCount; i++)
			{
				float3 p = positions[RingIndex(i, origin, cycleCount) * vpr + j];
				projected[i] = new float2(math.dot(p, e1), math.dot(p, e2));
			}

			float cellSize = MinCellSize;
			for (int s = 0; s < segCount; s++)
				cellSize = math.max(cellSize, math.distance(projected[s], projected[(s + 1) % cycleCount]));

			cells.Clear();
			for (int s = 0; s < segCount; s++)
				InsertSegment(cells, projected[s], projected[(s + 1) % cycleCount], cellSize, s);

			int current = 0;
			while (current < segCount - 2)
			{
				float2 a0 = projected[current];
				float2 a1 = projected[(current + 1) % cycleCount];
				if (math.distancesq(a0, a1) < ParallelEpsilon)
				{
					current++;
					continue;
				}

				CollectCandidates(cells, a0, a1, cellSize, candidates);
				candidates.Sort();

				int hitSegment = -1;
				float3 hitPoint = float3.zero;
				foreach (int k in candidates)
				{
					if (k <= current + 1 || k >= segCount)
						continue;
					if (closed && current == 0 && k == segCount - 1)
						continue;
					if (turnAt[k] - turnAt[current] > TurnLimit)
						continue;

					float2 b0 = projected[k];
					float2 b1 = projected[(k + 1) % cycleCount];
					if (math.distancesq(b0, b1) < ParallelEpsilon)
						continue;

					progressCounter++;
					if (progressCounter % 1024 == 0)
					{
						ct.ThrowIfCancellationRequested();
						reportProgress();
					}

					if (!TrySegmentIntersection(a0, a1, b0, b1, out float ta, out float tb))
						continue;

					int ia = RingIndex(current, origin, cycleCount) * vpr + j;
					int ia1 = RingIndex((current + 1) % cycleCount, origin, cycleCount) * vpr + j;
					int ib = RingIndex(k, origin, cycleCount) * vpr + j;
					int ib1 = RingIndex((k + 1) % cycleCount, origin, cycleCount) * vpr + j;
					float3 pa = math.lerp(positions[ia], positions[ia1], ta);
					float3 pb = math.lerp(positions[ib], positions[ib1], tb);
					if (math.distance(pa, pb) > bridgeTolerance)
						continue;

					hitSegment = k;
					hitPoint = (pa + pb) * 0.5f;
					break;
				}

				if (hitSegment < 0)
				{
					current++;
					continue;
				}

				float2 snapProjected = new float2(math.dot(hitPoint, e1), math.dot(hitPoint, e2));
				for (int m = current + 1; m <= hitSegment; m++)
				{
					positions[RingIndex(m, origin, cycleCount) * vpr + j] = hitPoint;
					projected[m] = snapProjected;
				}

				current = hitSegment;
			}
		}
	}

	if (closed)
	{
		for (int j = 0; j < vpr; j++)
			positions[(ringCount - 1) * vpr + j] = positions[j];
	}
}

private static int RingIndex(int index, int origin, int cycleCount)
{
	int shifted = index + origin;
	if (shifted >= cycleCount)
		shifted -= cycleCount;
	return shifted;
}

private static bool TrySegmentIntersection(float2 a0, float2 a1, float2 b0, float2 b1, out float ta, out float tb)
{
	ta = 0f;
	tb = 0f;
	float2 d1 = a1 - a0;
	float2 d2 = b1 - b0;
	float den = d1.x * d2.y - d1.y * d2.x;
	if (math.abs(den) < ParallelEpsilon)
		return false;

	float2 dp = b0 - a0;
	ta = (dp.x * d2.y - dp.y * d2.x) / den;
	tb = (dp.x * d1.y - dp.y * d1.x) / den;
	return ta >= -ParamSlack && ta <= 1f + ParamSlack && tb >= -ParamSlack && tb <= 1f + ParamSlack;
}

private static void InsertSegment(Dictionary<long, List<int>> cells, float2 a, float2 b, float cellSize, int segment)
{
	int x0 = (int)math.floor(math.min(a.x, b.x) / cellSize);
	int x1 = (int)math.floor(math.max(a.x, b.x) / cellSize);
	int y0 = (int)math.floor(math.min(a.y, b.y) / cellSize);
	int y1 = (int)math.floor(math.max(a.y, b.y) / cellSize);

	for (int x = x0; x <= x1; x++)
	{
		for (int y = y0; y <= y1; y++)
		{
			long key = ((long)x << 32) ^ (uint)y;
			if (!cells.TryGetValue(key, out var list))
			{
				list = new List<int>();
				cells.Add(key, list);
			}
			list.Add(segment);
		}
	}
}

private static void CollectCandidates(Dictionary<long, List<int>> cells, float2 a, float2 b, float cellSize, List<int> candidates)
{
	candidates.Clear();
	int x0 = (int)math.floor(math.min(a.x, b.x) / cellSize) - 1;
	int x1 = (int)math.floor(math.max(a.x, b.x) / cellSize) + 1;
	int y0 = (int)math.floor(math.min(a.y, b.y) / cellSize) - 1;
	int y1 = (int)math.floor(math.max(a.y, b.y) / cellSize) + 1;

	for (int x = x0; x <= x1; x++)
	{
		for (int y = y0; y <= y1; y++)
		{
			long key = ((long)x << 32) ^ (uint)y;
			if (cells.TryGetValue(key, out var list))
			{
				foreach (int segment in list)
				{
					if (!candidates.Contains(segment))
						candidates.Add(segment);
				}
			}
		}
	}
}
```

Свойства, которые обязаны сохраниться при реализации:

- Кандидаты сортируются, берётся ближайшее вперёд пересечение — порядок обхода фиксирован, результат детерминирован.
- Снап-точка лежит на под-отрезках исходных сегментов — новые рёбра не порождают новых пересечений, пространственный хеш строится один раз на прогон колонки.
- Кольца вне петель не двигаются вовсе; наружная сторона поворота и прямые участки нетронуты.
- Защита поворотом (`TurnLimit`) пропускает шпильку (поворот меньше 270 градусов) и отсекает перекрёстки; защита `BridgeTolerance` отсекает проходы сплайна над собой.
- Для closed-сплайна оба прогона работают по общим позициям, шов копируется из кольца 0 после всех колонок.

---

## Документация

В `Documentation~/Sweep-Addon.md` пункт Behavior о триме заменить: складки удаляются по самопересечению офсетных ломаных колонок профиля — внутренние кольца петли снапаются в точку пересечения, кромки сходятся в неё же и продолжаются швом; перекрытия без складки (перекрёстки, мосты, наложение плеч вдали от складки) не тримятся.

---

## Приёмка

- Шпилька-V с радиусом меньше полуширины (сцена скрина): нет щели вдоль биссектрисы, нет пересечений треугольников; внутренние кромки плеч сходятся в точке пересечения и продолжаются швом до центра кривизны; веер сплошной.
- U-разворот: веер вокруг центра кривизны, наружная кромка гладкая, кромки непрерывны.
- Прямой сплайн и пологая дуга: ни одна вершина не двигается, вывод идентичен сырому.
- P-образный сплайн (стебель пересекает петлю): трим не срабатывает, перекрёсток остаётся как есть.
- Сплайн-мост (проход над собой с зазором больше половины бокового вылета): трим не срабатывает.
- Closed-шпилька со складкой через шов: складка удаляется вторым прогоном, позиции кольца 0 и шва идентичны бит-в-бит.
- Rectangle и HalfPipe: каждая колонка тримится по собственной ломаной, вертикальная форма профиля сохраняется.
- Детерминизм: повторное вычисление на тех же входах даёт идентичные массивы; отмена в любой точке не оставляет частичных объектов.

---

## После выполнения

- Смени статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта (`Docs/PROJECT_MAP.md`, справка нод) под внесённые изменения.
