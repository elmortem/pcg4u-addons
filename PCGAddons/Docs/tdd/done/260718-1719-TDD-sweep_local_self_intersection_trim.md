# ТДД: Sweep — трим локальных самопересечений и адаптивный шаг

Status: Выполнено

Sweep-меш самопересекается на внутренней стороне поворотов, когда радиус кривизны меньше половины ширины профиля: кольца веером налезают друг на друга. Лечение — клиппинг вершин по плоскостям соседних колец (дискретная огибающая), адаптивная субдивизия шага по углу поворота тангенса и чистка дегенератной геометрии. Дальние самопересечения (сплайн лёг сам на себя, X/T-стыки разных сплайнов) — тема отдельного ТДД про junction-патчи, здесь не затрагиваются.

Тип работ: рефакторинг алгоритма существующей ноды. Внешние контракты (`MeshInstanceData`, снапшот → пул потоков, драпировка, капы, LUT-кривые) сохраняются.

---

## Файлы

Изменяются:

- `Packages/PCG.Sweep/Scripts/Sweep/SweepSplineNode.cs` — поле `MaxAngle`.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSplineNodeExecutor.cs` — адаптивная субдивизия в `BuildFrames`.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepMeshBuilder.cs` — фазовая перестройка `Build`, клиппинг, чистка.
- `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — поле `MaxAngle`, описание трима.

Создаётся:

- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepWeldKey.cs` — ключ сварки вершин.

---

## Нода: поле MaxAngle

В `SweepSplineNode` после поля `Step` добавить:

```csharp
[Input]
[PcgMemberInfo("Maximum tangent turn in degrees between adjacent rings.", Tags = new[] { "angle", "adaptive" })]
public float MaxAngle = 5f;
```

Инвалидация штатная через `ParamVersion`, в `GetVersionSalt` ничего не добавляется.

---

## Executor: адаптивная субдивизия колец

В `BuildSnapshot` читать значение и передавать в `BuildFrames`:

```csharp
float maxAngle = math.clamp(GetInputValue(nameof(Data.MaxAngle), Data.MaxAngle), 0.5f, 180f);
```

`BuildFrames` получает новый параметр и перестраивается:

```csharp
private const float MinSubdivisionStep = 0.01f;
private const int MaxSubdivisionRounds = 24;

private SweepFrame[] BuildFrames(Spline spline, float length, float step, float maxAngle, int vpr)
{
	bool closed = spline.Closed;
	int steps = (int)math.ceil(length / step);
	steps = closed ? math.max(3, steps) : math.max(1, steps);
	int ringCount = steps + 1;

	if ((long)ringCount * vpr > MaxVerticesPerMesh)
	{
		Debug.LogError($"[Sweep Spline] A spline would build {(long)ringCount * vpr} vertices which exceeds the {MaxVerticesPerMesh} limit; it was skipped.");
		return null;
	}

	var frames = new List<SweepFrame>(ringCount);
	for (int i = 0; i <= steps; i++)
	{
		float distance = length * i / steps;
		if (!TryBuildFrame(spline, distance, length, out var frame))
			return null;
		frames.Add(frame);
	}

	if (!SubdivideFrames(spline, frames, length, maxAngle, vpr))
		return null;

	if (closed)
	{
		var seam = frames[0];
		seam.Distance = length;
		seam.T = 1f;
		frames[frames.Count - 1] = seam;
	}

	return frames.ToArray();
}

private bool SubdivideFrames(Spline spline, List<SweepFrame> frames, float length, float maxAngle, int vpr)
{
	float maxAngleRad = math.radians(maxAngle);
	long maxRings = MaxVerticesPerMesh / vpr;
	bool budgetHit = false;

	for (int round = 0; round < MaxSubdivisionRounds; round++)
	{
		var splitDistances = new List<float>();
		for (int i = 0; i < frames.Count - 1; i++)
		{
			float gap = frames[i + 1].Distance - frames[i].Distance;
			if (gap <= MinSubdivisionStep)
				continue;

			float3 t0 = math.normalizesafe(frames[i].Tangent, new float3(0f, 0f, 1f));
			float3 t1 = math.normalizesafe(frames[i + 1].Tangent, new float3(0f, 0f, 1f));
			float angle = math.acos(math.clamp(math.dot(t0, t1), -1f, 1f));
			if (angle > maxAngleRad)
				splitDistances.Add((frames[i].Distance + frames[i + 1].Distance) * 0.5f);
		}

		if (splitDistances.Count == 0)
			return true;

		if (frames.Count + splitDistances.Count > maxRings)
		{
			budgetHit = true;
			break;
		}

		foreach (float distance in splitDistances)
		{
			if (!TryBuildFrame(spline, distance, length, out var frame))
				return false;
			frames.Add(frame);
		}

		frames.Sort((a, b) => a.Distance.CompareTo(b.Distance));
	}

	if (budgetHit)
		Debug.LogWarning("[Sweep Spline] Adaptive subdivision stopped at the vertex limit; increase Step or MaxAngle to reduce density.");

	return true;
}
```

Свойства алгоритма, которые обязаны сохраниться при реализации:

- Раунд делит пополам все нарушающие интервалы сразу — плотность не смещается к началу сплайна, результат детерминирован.
- `MinSubdivisionStep` останавливает деление на изломах с разрывом тангенса (C0-узлы Bezier): там угол не уменьшается от субдивизии.
- Для closed-сплайна кадры на `t = 0` и `t = 1` совпадают геометрически, шов после субдивизии перезаписывается копией нулевого кадра — как раньше.

---

## Builder: фазы

`SweepMeshBuilder.Build` перестраивается в последовательность фаз. Подготовка (basis, капы) не меняется.

- Фаза «сырые вершины»: позиции `float3[] positions` и `Vector2[] uvs` для всех колец. Без семпла террейна. Формула позиции зависит от режима:
  - без террейна — как сейчас: `basePos + right * rx + up * ry`;
  - с террейном — `new float3(basePos.x + rightXz.x * rx, basePos.y + ry, basePos.z + rightXz.y * rx)`; дополнительно заполняется массив `float[] verticalOffsets` значением `ry` на вершину.
- Фаза «клиппинг»: `ClipRings` (ниже) правит `positions` на месте.
- Фаза «драпировка» (только при террейне): для каждой вершины `wy = h + snapshot.HeightOffset + verticalOffsets[idx]`, где `h` — билинейный семпл в `(pos.x, pos.z)`; при выходе за окно `pos.y` остаётся как есть (он уже равен `basePos.y + ry`), взводится `outOfBounds`. XZ не меняется.
- Фаза «треугольники и капы»: без изменений, работает по итоговым позициям.
- Фаза «чистка»: `Cleanup` (ниже) выдаёт финальные массивы для `SweepMeshData`.

Каждая фаза — каждые 1024 обработанных элемента `ct.ThrowIfCancellationRequested()` и `reportProgress()`.

---

## Клиппинг колец

Плоскость кольца `i` — точка `frames[i].Position`, нормаль — нормализованный `frames[i].Tangent`. Ограничения вершины кольца `i`: не впереди плоскости следующего кольца, не позади плоскости предыдущего. Нарушение лечится проекцией на плоскость. Пересечение полупространств выпукло, циклические проекции сходятся; проходы вперёд и назад повторяются до фикспойнта с потолком итераций.

```csharp
private const float ClipEpsilon = 1e-5f;
private const int MaxClipPasses = 32;

private static void ClipRings(SweepFrame[] frames, float3[] positions, int vpr, bool closed, CancellationToken ct, Action reportProgress)
{
	int ringCount = frames.Length;
	int lastRing = closed ? ringCount - 2 : ringCount - 1;
	if (lastRing < 1)
		return;

	var normals = new float3[ringCount];
	for (int i = 0; i < ringCount; i++)
		normals[i] = math.normalizesafe(frames[i].Tangent, new float3(0f, 0f, 1f));

	int progressCounter = 0;

	for (int pass = 0; pass < MaxClipPasses; pass++)
	{
		bool moved = false;

		for (int i = 0; i <= lastRing; i++)
		{
			int next = i == lastRing ? (closed ? 0 : -1) : i + 1;
			if (next >= 0)
				moved |= ClampRing(positions, i, vpr, frames[next].Position, normals[next], 1f, ref progressCounter, ct, reportProgress);
		}

		for (int i = lastRing; i >= 0; i--)
		{
			int prev = i == 0 ? (closed ? lastRing : -1) : i - 1;
			if (prev >= 0)
				moved |= ClampRing(positions, i, vpr, frames[prev].Position, normals[prev], -1f, ref progressCounter, ct, reportProgress);
		}

		if (!moved)
			break;
	}

	if (closed)
	{
		for (int j = 0; j < vpr; j++)
			positions[(ringCount - 1) * vpr + j] = positions[j];
	}
}

private static bool ClampRing(float3[] positions, int ring, int vpr, float3 planePoint, float3 planeNormal, float side, ref int progressCounter, CancellationToken ct, Action reportProgress)
{
	bool moved = false;
	for (int j = 0; j < vpr; j++)
	{
		int idx = ring * vpr + j;
		float d = math.dot(positions[idx] - planePoint, planeNormal) * side;
		if (d > ClipEpsilon)
		{
			positions[idx] -= planeNormal * (d * side);
			moved = true;
		}

		progressCounter++;
		if (progressCounter % 1024 == 0)
		{
			ct.ThrowIfCancellationRequested();
			reportProgress();
		}
	}
	return moved;
}
```

Нюансы:

- Для closed-сплайна кольца клиппуются в диапазоне `0..ringCount-2` с циклическими соседями, затем позиции шва копируются из кольца 0 — шов остаётся сваренным бит-в-бит при разных UV.
- Прямые участки и внешняя сторона поворота не двигаются вовсе: их вершины ограничения не нарушают.
- Twist, width/height-кривые и капы не требуют спецобработки: клиппинг оперирует готовыми позициями, капы копируют уже зажатые вершины.

---

## Чистка

Ключ сварки — отдельный файл `SweepWeldKey.cs`:

```csharp
using System;

namespace PCG.Sweep
{
	public readonly struct SweepWeldKey : IEquatable<SweepWeldKey>
	{
		public readonly long Px;
		public readonly long Py;
		public readonly long Pz;
		public readonly long Ux;
		public readonly long Uy;

		public SweepWeldKey(UnityEngine.Vector3 position, UnityEngine.Vector2 uv)
		{
			Px = (long)Math.Round(position.x * 100000d);
			Py = (long)Math.Round(position.y * 100000d);
			Pz = (long)Math.Round(position.z * 100000d);
			Ux = (long)Math.Round(uv.x * 100000d);
			Uy = (long)Math.Round(uv.y * 100000d);
		}

		public bool Equals(SweepWeldKey other)
		{
			return Px == other.Px && Py == other.Py && Pz == other.Pz && Ux == other.Ux && Uy == other.Uy;
		}

		public override bool Equals(object obj)
		{
			return obj is SweepWeldKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = Px.GetHashCode();
				hash = (hash * 397) ^ Py.GetHashCode();
				hash = (hash * 397) ^ Pz.GetHashCode();
				hash = (hash * 397) ^ Ux.GetHashCode();
				hash = (hash * 397) ^ Uy.GetHashCode();
				return hash;
			}
		}
	}
}
```

`Cleanup` в `SweepMeshBuilder`, вызывается последней фазой, всегда:

```csharp
private const float MinTriangleArea = 1e-8f;

private static void Cleanup(ref Vector3[] vertices, ref Vector2[] uvs, ref int[] triangles, CancellationToken ct)
```

Алгоритм:

- Сварка: обход вершин по возрастанию индекса, `Dictionary<SweepWeldKey, int>`; `remap[i]` — индекс первого вхождения ключа. Вершины веера с одной позицией, но разным V, имеют разные ключи и не свариваются — сжатие текстуры к точке складки сохраняется.
- Фильтр треугольников: обход троек по порядку, индексы через `remap`; тройка отбрасывается, если два индекса совпали или `0.5f * length(cross(b - a, c - a)) < MinTriangleArea` по позициям.
- Компакция: обход выживших троек по порядку, вершине при первом использовании выдаётся новый индекс; финальные массивы `vertices`, `uvs`, `triangles` пересобираются. Порядок обходов фиксирован — результат детерминирован.
- Каждые 1024 тройки — `ct.ThrowIfCancellationRequested()`.

---

## Документация

В `Documentation~/Sweep-Addon.md`:

- В список полей `Sweep Spline` после `Step` добавить `Max Angle` — максимальный поворот тангенса в градусах между соседними кольцами; дуги уплотняются субдивизией, прямые участки остаются на шаге `Step`.
- В раздел Behavior добавить пункт: кольца клиппуются по плоскостям соседних колец, внутренняя сторона крутого поворота складывается в веер без самопересечений; дегенератные треугольники и дублирующиеся вершины удаляются.

---

## Приёмка

- Прямой сплайн: вершины, UV и треугольники идентичны выводу до правок (субдивизия не срабатывает, клиппинг не двигает вершины, чистка ничего не удаляет).
- Пологая дуга (радиус кривизны больше полуширины): клиппинг не двигает вершины; плотность колец соответствует `MaxAngle`.
- U-разворот с радиусом меньше полуширины (сцена скрина): внутренняя сторона — веер вокруг центра кривизны, ни одна вершина кольца не нарушает ограничение соседних плоскостей больше `ClipEpsilon`, треугольников с площадью меньше `MinTriangleArea` в выводе нет.
- Closed-сплайн: позиции кольца 0 и шва идентичны бит-в-бит.
- Излом с разрывом тангенса: субдивизия завершается за счёт `MinSubdivisionStep`, зависаний нет.
- Бюджет: суммарное число вершин никогда не превышает `MaxVerticesPerMesh`; при упоре в лимит — одно предупреждение.
- Детерминизм: повторное вычисление на тех же входах даёт идентичные массивы.
- Изменение `MaxAngle` инвалидирует ноду и пересчитывает результат; отмена во время любой фазы не оставляет частичных объектов в сцене.
- Террейн-режим: XZ вершин после драпировки совпадает с XZ после клиппинга; вне окна террейна вершина сохраняет высоту сплайн-кадра и логируется одно предупреждение.

---

## После выполнения

- Смени статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта (`Docs/PROJECT_MAP.md`, справка нод) под внесённые изменения.
