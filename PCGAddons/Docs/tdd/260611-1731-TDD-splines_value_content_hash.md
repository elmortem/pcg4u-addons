Status: Выполнено

# TDD: Хеш контента SplinesValue учитывает узлы и трансформ контейнера

## Проблема

При перемещении узлов сплайна (knots) в `SplineContainer`, проброшенном в граф через `SplinesValue`, перегенерация графа не даёт эффекта: инстансы остаются на старых позициях.

Причина — в `SplinesValue.GetContentHash()`. В хеш входят только:

- `Containers.Count`
- `container.GetInstanceID()`
- `container.transform.position`
- `spline.Count` (число узлов в каждом сплайне)

Позиции, касательные и повороты узлов в хеш не входят. Поворот и масштаб контейнера тоже не входят. При сдвиге узлов (без изменения их количества и позиции контейнера) хеш не меняется, ядро PCG считает вход неизменным и переиспользует закешированный результат вниз по графу. Перегенерация не происходит.

## Решение

В `SplinesValue.GetContentHash()` добавить в хеш:

- полную матрицу `container.transform.localToWorldMatrix` вместо одной `position`;
- по каждому сплайну — флаг `Closed`;
- по каждому узлу — `Position`, `TangentIn`, `TangentOut`, `Rotation` и tangent mode (`spline.GetTangentMode(index)`).

## Файл

- `Packages/PCG.Splines/Scripts/Values/SplinesValue.cs`

Меняется только метод `GetContentHash()`. Остальной код файла не трогаем.

## Реализация

Заменить тело метода `GetContentHash()` на:

```csharp
public override int GetContentHash()
{
	unchecked
	{
		int hash = Containers.Count;
		for (int i = 0; i < Containers.Count; i++)
		{
			var container = Containers[i];
			hash = (hash * 397) ^ (container != null ? container.GetInstanceID() : 0);
			if (container != null)
			{
				hash = (hash * 397) ^ container.transform.localToWorldMatrix.GetHashCode();
				foreach (var spline in container.Splines)
				{
					hash = (hash * 397) ^ spline.Count;
					hash = (hash * 397) ^ spline.Closed.GetHashCode();
					for (int k = 0; k < spline.Count; k++)
					{
						var knot = spline[k];
						hash = (hash * 397) ^ knot.Position.GetHashCode();
						hash = (hash * 397) ^ knot.TangentIn.GetHashCode();
						hash = (hash * 397) ^ knot.TangentOut.GetHashCode();
						hash = (hash * 397) ^ knot.Rotation.GetHashCode();
						hash = (hash * 397) ^ (int)spline.GetTangentMode(k);
					}
				}
			}
		}

		return hash;
	}
}
```

## Проверка

- Прокинуть `SplineContainer` в граф через `SplinesValue`, сгенерировать инстансы.
- Сдвинуть один узел сплайна, запустить перегенерацию — инстансы должны встать по новым позициям.
- Изменить только касательную узла (tangent) — перегенерация должна сработать.
- Повернуть/смасштабировать контейнер сплайна — перегенерация должна сработать.

---

## После выполнения

- Поменяй статус вверху документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта под внесённые изменения.
