# ТДД: Sweep — квантовый шаг и оконный клиппинг

Status: Выполнено

Ложится поверх выполненного `260718-1719-TDD-sweep_local_self_intersection_trim.md` и заменяет два его механизма:

- Субдивизия ниже `Step` отбирает у пользователя контроль над плотностью. Новая семантика: `Step` — минимальный шаг и квант, мельче которого кольца не ставятся никогда; адаптивность работает в другую сторону — прореживает прямые участки до `MaxStep`, пока накопленный поворот не превысит `MaxAngle`. Худший случай — ровно равномерная сетка `ceil(length / Step) + 1` колец.
- Клиппинг только по соседним плоскостям пропускает глубокие складки: вершина кольца удовлетворяет плоскостям `i±1`, но пересекает плоскости `i±3`, `i±5` — над веером остаются кресты треугольников. Новый клиппинг зажимает вершину по окну колец, ограниченному хордой и накопленным поворотом.

---

## Файлы

Изменяются:

- `Packages/PCG.Sweep/Scripts/Sweep/SweepSplineNode.cs` — поле `MaxStep`, описания `Step` и `MaxAngle`.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSplineNodeExecutor.cs` — марш по квантам вместо субдивизии, вынос бокового вылета в снапшот.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepSnapshot.cs` — поле `MaxLateralExtent`.
- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepMeshBuilder.cs` — оконный `ClipRings`.
- `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — семантика `Step`/`MaxStep`/`MaxAngle`, поведение клиппинга.

Удаляются: `SubdivideFrames`, константы `MinSubdivisionStep` и `MaxSubdivisionRounds`, предупреждение о лимите субдивизии.

---

## Нода

В `SweepSplineNode` описание `Step` заменить и после него добавить `MaxStep`:

```csharp
[Input]
[PcgMemberInfo("Minimum length of a sweep segment; rings snap to multiples of this quantum.", Tags = new[] { "step" })]
public float Step = 1f;

[Input]
[PcgMemberInfo("Maximum length of a sweep segment on straight sections.", Tags = new[] { "step", "max" })]
public float MaxStep = 8f;
```

Описание `MaxAngle` заменить:

```csharp
[Input]
[PcgMemberInfo("Maximum accumulated tangent turn in degrees before the next ring is emitted.", Tags = new[] { "angle", "adaptive" })]
public float MaxAngle = 5f;
```

Инвалидация всех трёх — штатная через `ParamVersion`.

---

## Executor: марш по квантам

В `BuildSnapshot`:

```csharp
float step = math.max(0.05f, GetInputValue(nameof(Data.Step), Data.Step));
float maxStep = math.max(step, GetInputValue(nameof(Data.MaxStep), Data.MaxStep));
```

Вычисление бокового вылета переносится из террейн-блока выше, до цикла по сплайнам, и используется в обоих местах:

```csharp
float maxAbsProfile = 0f;
for (int i = 0; i < profile.Points.Length; i++)
	maxAbsProfile = math.max(maxAbsProfile, math.length(profile.Points[i]));

float maxMul = math.max(MaxLut(widthLut), MaxLut(heightLut));
float lateralExtent = maxAbsProfile * maxMul;
```

Террейн-блок использует `lateralExtent` как `margin`. В снапшот пишется новое поле:

```csharp
MaxLateralExtent = lateralExtent,
```

В `SweepSnapshot` добавить:

```csharp
public float MaxLateralExtent;
```

`BuildFrames` заменяется целиком; `SubdivideFrames` и его константы удаляются:

```csharp
private SweepFrame[] BuildFrames(Spline spline, float length, float step, float maxStep, float maxAngle, int vpr)
{
	bool closed = spline.Closed;
	int quantCount = (int)math.ceil(length / step);
	quantCount = closed ? math.max(3, quantCount) : math.max(1, quantCount);

	if ((long)(quantCount + 1) * vpr > MaxVerticesPerMesh)
	{
		Debug.LogError($"[Sweep Spline] A spline would build {(long)(quantCount + 1) * vpr} vertices which exceeds the {MaxVerticesPerMesh} limit; it was skipped.");
		return null;
	}

	var quantFrames = new SweepFrame[quantCount + 1];
	for (int q = 0; q <= quantCount; q++)
	{
		float distance = length * q / quantCount;
		if (!TryBuildFrame(spline, distance, length, out quantFrames[q]))
			return null;
	}

	var turns = new float[quantCount];
	for (int q = 0; q < quantCount; q++)
	{
		float3 t0 = math.normalizesafe(quantFrames[q].Tangent, new float3(0f, 0f, 1f));
		float3 t1 = math.normalizesafe(quantFrames[q + 1].Tangent, new float3(0f, 0f, 1f));
		turns[q] = math.acos(math.clamp(math.dot(t0, t1), -1f, 1f));
	}

	float maxAngleRad = math.radians(maxAngle);
	var frames = new List<SweepFrame>(quantCount + 1);
	frames.Add(quantFrames[0]);

	int current = 0;
	while (current < quantCount)
	{
		int next = current + 1;
		float turnSum = turns[current];
		while (next < quantCount)
		{
			float candidateTurn = turnSum + turns[next];
			float candidateLength = quantFrames[next + 1].Distance - quantFrames[current].Distance;
			if (candidateTurn > maxAngleRad || candidateLength > maxStep)
				break;
			turnSum = candidateTurn;
			next++;
		}

		frames.Add(quantFrames[next]);
		current = next;
	}

	if (closed)
	{
		var seam = frames[0];
		seam.Distance = length;
		seam.T = 1f;
		frames[frames.Count - 1] = seam;
	}

	return frames.ToArray();
}
```

Свойства, которые обязаны сохраниться при реализации:

- Кольца лежат только на квантовой сетке `length * q / quantCount`; первое — на `0`, последнее — ровно на `length`.
- Минимальный шаг марша — один квант: даже излом с разрывом тангенса даёт кольцо не ближе кванта, зависаний нет.
- Число колец никогда не превышает `quantCount + 1`; проверка лимита вершин выполняется по полной сетке до марша.
- Прореживание зависит только от геометрии сплайна и параметров — результат детерминирован.

---

## Builder: оконный клиппинг

Сигнатура `ClipRings` получает боковой вылет; вызов в `Build`:

```csharp
ClipRings(frames, positions, vpr, splineClosed, snapshot.MaxLateralExtent, ct, reportProgress);
```

Окно кольца `i` в каждом направлении: ближайший сосед входит всегда; окно расширяется на следующее кольцо, пока накопленный поворот нормалей от `i` не превысил 180 градусов и хорда от `frames[i].Position` до кандидата не превысила `MaxLateralExtent * ChordSafety`. Ограничение по повороту защищает витки и замкнутые контуры от ложного зажима, ограничение по хорде отсекает кольца, чьи плоскости физически не достают до вершин кольца `i`. Вершина кольца `i` обязана быть позади каждой плоскости переднего окна и впереди каждой плоскости заднего.

`ClipRings` заменяется целиком, `ClampRing` не меняется:

```csharp
private const float ClipEpsilon = 1e-5f;
private const int MaxClipPasses = 32;
private const float ChordSafety = 1.05f;

private static void ClipRings(SweepFrame[] frames, float3[] positions, int vpr, bool closed, float lateralExtent, CancellationToken ct, Action reportProgress)
{
	int ringCount = frames.Length;
	int cycleCount = closed ? ringCount - 1 : ringCount;
	if (cycleCount < 2)
		return;

	var normals = new float3[cycleCount];
	for (int i = 0; i < cycleCount; i++)
		normals[i] = math.normalizesafe(frames[i].Tangent, new float3(0f, 0f, 1f));

	float chordLimit = lateralExtent * ChordSafety;
	var forwardCounts = new int[cycleCount];
	var backwardCounts = new int[cycleCount];
	for (int i = 0; i < cycleCount; i++)
	{
		forwardCounts[i] = ExpandWindow(frames, normals, cycleCount, closed, chordLimit, i, 1);
		backwardCounts[i] = ExpandWindow(frames, normals, cycleCount, closed, chordLimit, i, -1);
	}

	int progressCounter = 0;

	for (int pass = 0; pass < MaxClipPasses; pass++)
	{
		bool moved = false;

		for (int i = 0; i < cycleCount; i++)
		{
			for (int s = 1; s <= forwardCounts[i]; s++)
			{
				int m = Wrap(i + s, cycleCount, closed);
				moved |= ClampRing(positions, i, vpr, frames[m].Position, normals[m], 1f, ref progressCounter, ct, reportProgress);
			}

			for (int s = 1; s <= backwardCounts[i]; s++)
			{
				int m = Wrap(i - s, cycleCount, closed);
				moved |= ClampRing(positions, i, vpr, frames[m].Position, normals[m], -1f, ref progressCounter, ct, reportProgress);
			}
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

private static int ExpandWindow(SweepFrame[] frames, float3[] normals, int cycleCount, bool closed, float chordLimit, int start, int direction)
{
	int maxSteps = closed ? cycleCount - 1 : (direction > 0 ? cycleCount - 1 - start : start);
	if (maxSteps < 1)
		return 0;

	int count = 1;
	int current = Wrap(start + direction, cycleCount, closed);
	float turn = math.acos(math.clamp(math.dot(normals[start], normals[current]), -1f, 1f));

	while (count < maxSteps)
	{
		int next = Wrap(current + direction, cycleCount, closed);
		turn += math.acos(math.clamp(math.dot(normals[current], normals[next]), -1f, 1f));
		if (turn > math.PI)
			break;
		if (math.distance(frames[next].Position, frames[start].Position) > chordLimit)
			break;

		count++;
		current = next;
	}

	return count;
}

private static int Wrap(int index, int cycleCount, bool closed)
{
	if (!closed)
		return index;
	return (index % cycleCount + cycleCount) % cycleCount;
}
```

Свойства, которые обязаны сохраниться при реализации:

- Ближайший сосед в окне всегда, независимо от хорды: редкие кольца на резком изломе зажимаются, как раньше.
- Пересечение полупространств окна выпукло — циклические проекции сходятся, фикспойнт с потолком `MaxClipPasses` сохраняется.
- Наружная сторона поворота ограничения не нарушает и не двигается; для closed-сплайна окна циклические, шов копируется из кольца 0 после клиппинга.

---

## Документация

В `Documentation~/Sweep-Addon.md`:

- `Step` — минимальный шаг и квант: кольца ставятся только на кратных ему дистанциях, мельче кольца не бывают, максимум вершин предсказуем как `ceil(length / Step) + 1` колец.
- `Max Step` — потолок прореживания на прямых участках; при драпировке держит облегание террейна.
- `Max Angle` — накопленный поворот тангенса, после которого обязано появиться следующее кольцо.
- В Behavior: кольца зажимаются плоскостями окна соседних колец (по хорде бокового вылета и повороту до 180 градусов) — глубокие складки собираются в веер без остаточных пересечений.

---

## Приёмка

- Прямой сплайн: кольца через `MaxStep` (последний интервал короче), клиппинг и чистка ничего не меняют.
- Число колец никогда не превышает `ceil(length / Step) + 1`; все кольца на квантовой сетке, последнее ровно на конце сплайна.
- U-разворот с радиусом меньше полуширины (сцена скрина): после клиппинга ни одна вершина не нарушает ни одну плоскость своего окна больше `ClipEpsilon`; визуально нет пересечений треугольников ни в веере, ни над ним.
- Резкий излом при кольцах реже бокового вылета: сосед всё равно в окне, пересечений нет.
- Closed-сплайн: позиции кольца 0 и шва идентичны, полный оборот не зажимает наружные кольца (окно ограничено 180 градусами поворота).
- `MaxStep` меньше `Step` санируется до `Step`; изменение `MaxStep` инвалидирует ноду.
- Детерминизм: повторное вычисление на тех же входах даёт идентичные массивы; отмена в любой фазе не оставляет частичных объектов.

---

## После выполнения

- Смени статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта (`Docs/PROJECT_MAP.md`, справка нод) под внесённые изменения.
