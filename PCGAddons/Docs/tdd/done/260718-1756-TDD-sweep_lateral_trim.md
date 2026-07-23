# ТДД: Sweep — боковой трим вместо проекции на плоскости

Status: Выполнено

Ложится поверх выполненного `260718-1743-TDD-sweep_quantum_step_window_clipping.md`. Артефакт: проекция вершины на нарушенную плоскость двигает её вдоль тангенса чужого кольца — продольно. На входе в складку это отрывает вершины от собственного кольца и рвёт покрытие: над веером остаётся щель, а обрезанная кромка не сходится с кромками прямых участков. Правильный примитив — трим вдоль собственного кольца: вершина сдвигается к осевой линии по своему боковому лучу ровно до границы допустимой области. Кольцо остаётся кольцом, покрытие непрерывно от осевой до границы трима, кромки прямых участков и веер сходятся в общий шов. Ограничения линейны вдоль луча, поэтому решение считается за один проход без итераций до фикспойнта.

Окна колец (`ExpandWindow`, `Wrap`, хорда, поворот до 180 градусов) не меняются. Нода и executor не меняются.

---

## Файлы

Изменяются:

- `Packages/PCG.Sweep/Editor/Scripts/Exec/SweepMeshBuilder.cs` — фаза сырых вершин пишет боковые смещения, `ClipRings` и `ClampRing` заменяются на `TrimRings`.
- `Packages/PCG.Sweep/Documentation~/Sweep-Addon.md` — описание поведения трима.

Удаляются: `ClampRing`, константа `MaxClipPasses`.

---

## Фаза сырых вершин

Массив `verticalOffsets` заполняется всегда, а не только при террейне, и добавляется парный массив боковых смещений:

```csharp
var lateralOffsets = new float[ringVertexCount];
var verticalOffsets = new float[ringVertexCount];
```

Во внутреннем цикле по вершинам после вычисления `rx`/`ry` в обоих режимах:

```csharp
lateralOffsets[idx] = rx;
verticalOffsets[idx] = ry;
```

Формулы позиций не меняются. Вызов фазы клиппинга заменяется на:

```csharp
TrimRings(frames, rights, ups, positions, lateralOffsets, verticalOffsets, vpr, splineClosed, hasTerrain, snapshot.MaxLateralExtent, ct, reportProgress);
```

---

## Боковой трим

Вершина кольца `i` параметризуется боковым лучом своего кольца: `v(s) = base0 + dir * s`, где `base0` — точка осевой с вертикальным смещением профиля, `dir` — боковое направление в сторону вершины, `s` от `0` до `|rx|`. Требования к вершине прежние: позади каждой плоскости переднего окна, впереди каждой плоскости заднего. Каждая плоскость даёт линейное неравенство по `s`; берутся только ограничения сверху (тянущие к осевой), ограничения, толкающие наружу, отбрасываются — трим никогда не расширяет профиль. Итог — минимум по всем границам за один проход.

`ClipRings` и `ClampRing` заменяются целиком:

```csharp
private const float ClipEpsilon = 1e-5f;
private const float DirEpsilon = 1e-4f;
private const float ChordSafety = 1.05f;

private static void TrimRings(SweepFrame[] frames, float3[] rights, float3[] ups, float3[] positions, float[] lateralOffsets, float[] verticalOffsets, int vpr, bool closed, bool hasTerrain, float lateralExtent, CancellationToken ct, Action reportProgress)
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

	for (int i = 0; i < cycleCount; i++)
	{
		float3 lateralDir;
		float3 verticalDir;
		if (hasTerrain)
		{
			float2 rightXz = math.normalizesafe(new float2(rights[i].x, rights[i].z), new float2(1f, 0f));
			lateralDir = new float3(rightXz.x, 0f, rightXz.y);
			verticalDir = new float3(0f, 1f, 0f);
		}
		else
		{
			lateralDir = rights[i];
			verticalDir = ups[i];
		}

		for (int j = 0; j < vpr; j++)
		{
			int idx = i * vpr + j;
			float rx = lateralOffsets[idx];
			if (rx != 0f)
			{
				float3 dir = lateralDir * math.sign(rx);
				float3 base0 = frames[i].Position + verticalDir * verticalOffsets[idx];
				float s0 = math.abs(rx);
				float sMax = s0;

				for (int w = 1; w <= forwardCounts[i]; w++)
				{
					int m = Wrap(i + w, cycleCount, closed);
					float a = math.dot(base0 - frames[m].Position, normals[m]);
					float b = math.dot(dir, normals[m]);
					if (b > DirEpsilon)
						sMax = math.min(sMax, (ClipEpsilon - a) / b);
				}

				for (int w = 1; w <= backwardCounts[i]; w++)
				{
					int m = Wrap(i - w, cycleCount, closed);
					float a = math.dot(base0 - frames[m].Position, normals[m]);
					float b = math.dot(dir, normals[m]);
					if (b < -DirEpsilon)
						sMax = math.min(sMax, (-ClipEpsilon - a) / b);
				}

				positions[idx] = base0 + dir * math.clamp(sMax, 0f, s0);
			}

			progressCounter++;
			if (progressCounter % 1024 == 0)
			{
				ct.ThrowIfCancellationRequested();
				reportProgress();
			}
		}
	}

	if (closed)
	{
		for (int j = 0; j < vpr; j++)
			positions[(ringCount - 1) * vpr + j] = positions[j];
	}
}
```

Свойства, которые обязаны сохраниться при реализации:

- Вершина двигается только вдоль бокового луча своего кольца; продольных смещений нет, финальная позиция всегда между осевой и исходной позицией.
- Плоскость, почти параллельная лучу (`|b|` меньше `DirEpsilon`), и плоскость, чьё ограничение толкает наружу, границы не дают.
- Наружная сторона поворота и прямые участки не двигаются: их лучи не упираются ни в одну плоскость окна.
- Вертикальная составляющая профиля (`ry`) сохраняется при любом триме — стенки Rectangle и дуга HalfPipe не плющатся продольно.
- Один проход, без итераций; для closed-сплайна окна циклические, позиции шва копируются из кольца 0 после трима.

---

## Документация

В `Documentation~/Sweep-Addon.md` пункт Behavior о клиппинге заменить: в складках вершины подтягиваются к осевой вдоль собственного кольца до границы, заданной плоскостями окна соседних колец; внутренние кромки участков сходятся в общий шов, покрытие непрерывно, наружная кромка не меняется.

---

## Приёмка

- Шпилька с радиусом меньше полуширины (сцена скрина): нет ни щели над веером, ни пересечений треугольников; внутренние кромки двух прямых участков сходятся в точке и продолжаются швом по биссектрисе до центра кривизны; покрытие сплошное от осевой до кромки на всём протяжении.
- Каждая финальная вершина лежит на отрезке между `base0` и своей сырой позицией.
- Прямой сплайн и пологая дуга: вершины не двигаются, вывод идентичен сырому.
- Rectangle и HalfPipe на шпильке: вертикальные размеры профиля сохранены, тримится только боковая составляющая.
- Closed-сплайн: позиции кольца 0 и шва идентичны бит-в-бит; полный оборот наружные кольца не зажимает.
- Террейн-режим: трим горизонтален (по проекции right на XZ), драпировка по уже затримленным XZ.
- Детерминизм: повторное вычисление на тех же входах даёт идентичные массивы; отмена в любой точке не оставляет частичных объектов.

---

## После выполнения

- Смени статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта (`Docs/PROJECT_MAP.md`, справка нод) под внесённые изменения.
