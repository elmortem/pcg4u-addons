# ТДД: релизный процесс и тегирование аддонов

Status: Выполнено

> Дефолтная ветка репозитория `pcg4u-addons` называется `main`, а не `master`. Везде ниже `master` читается как `main`.

## Задача

Репозиторий `pcg4u-addons` получает релизную дисциплину веток и автоматическое тегирование версий пакетов. Каждый релиз аддона фиксируется git-тегом `<packageName>/<version>`, по которому ядро ставит пакеты пиннутыми. Внешние репозитории зависимостей получают такие же теги вручную.

## Ветки

Создать ветку `develop`:

```
git checkout master
git pull
git checkout -b develop
git push -u origin develop
```

Правила:

- Повседневная работа ведётся в `develop`.
- `master` всегда содержит состояние, совместимое с последним публично доступным в Asset Store ядром PCG4U.
- Мерж `develop` → `master` выполняется только после того, как соответствующий релиз ядра прошёл апрув и стал доступен пользователям.

## Правила версий

- Каждый мерж в `master`, меняющий содержимое пакета, сопровождается бампом `version` в `package.json` этого пакета.
- При бампе версии аддона его новый номер прописывается в `dependencies` зависимых аддонов.
- Тег `<name>/<version>` создаётся автоматически экшеном, руками теги в этом репозитории не ставятся.

## GitHub Action

Новый файл в корне репозитория `pcg4u-addons` (не внутри `PCGAddons`): `.github/workflows/tag-packages.yml`:

```yaml
name: Tag packages

on:
  workflow_dispatch:
  push:
    branches:
      - master
    paths:
      - "PCGAddons/Packages/**/package.json"

permissions:
  contents: write

jobs:
  tag:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
          fetch-tags: true
      - name: Create missing tags
        run: |
          created=0
          for manifest in PCGAddons/Packages/*/package.json; do
            name=$(jq -r '.name' "$manifest")
            version=$(jq -r '.version' "$manifest")
            case "$name" in
              com.elmortem.*) ;;
              *) continue ;;
            esac
            tag="$name/$version"
            if git rev-parse -q --verify "refs/tags/$tag" > /dev/null; then
              continue
            fi
            git tag "$tag"
            created=1
          done
          if [ "$created" = "1" ]; then
            git push origin --tags
          fi
```

## Первичное тегирование

- Закоммитить workflow в `master`, запушить.
- На GitHub открыть Actions → `Tag packages` → `Run workflow` (ветка `master`).
- Проверить, что созданы теги: `com.elmortem.pcg.splines/0.0.7`, `com.elmortem.pcg.spriteshapes/0.0.3`, `com.elmortem.pcg.polygons/0.0.6`, `com.elmortem.pcg.mazes/0.0.3`, `com.elmortem.pcg.octree/0.0.2`, `com.elmortem.pcg.brg/0.0.2`, `com.elmortem.pcg.sweep/0.0.5`.

## Синхронизация dependencies

В `develop` поправить `package.json` пакетов — везде `com.elmortem.pcg.splines: 0.0.6` заменить на `0.0.7`:

- `PCGAddons/Packages/PCG.Mazes/package.json`
- `PCGAddons/Packages/PCG.Polygons/package.json`
- `PCGAddons/Packages/PCG.SpriteShapes/package.json`
- `PCGAddons/Packages/PCG.Sweep/package.json`

## Внешние репозитории зависимостей

Отменено 08.08.2026 решением заказчика: `com.elmortem.delone`, `com.elmortem.octree` и `com.elmortem.brg` остаются без тегов и ставятся с ветки. Ниже — исходный план на случай, если теги там всё же понадобятся.

Одноразово поставить теги вручную, формат тот же `<packageName>/<version>`:

- В `elmortem/triangulation-delone`:

```
git tag com.elmortem.delone/0.0.1
git push origin com.elmortem.delone/0.0.1
```

- В `elmortem/octree`:

```
git tag com.elmortem.octree/0.0.3
git push origin com.elmortem.octree/0.0.3
```

- В `elmortem/brg`:

```
git tag com.elmortem.brg/0.0.2
git push origin com.elmortem.brg/0.0.2
```

Правило: при бампе `version` в этих репозиториях тег ставится вручную тем же форматом.

## Проверка

- `git ls-remote --tags origin` в каждом репозитории показывает все перечисленные теги.
- В тестовом Unity-проекте пакет ставится по URL с тегом, например `https://github.com/elmortem/pcg4u-addons.git?path=PCGAddons/Packages/PCG.Splines#com.elmortem.pcg.splines/0.0.7`, и Package Manager показывает версию `0.0.7`.

## После выполнения

- Смени статус в начале документа на `Выполнено`.
- Уточни у заказчика, нужно ли обновить документацию проекта под эти изменения.
