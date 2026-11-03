#!/bin/sh
# Атомарная выкатка сборки клиента или админки (спец. infrastructure, 7) — та же схема,
# что у справки в publish-help.yml:
#
#   <корень>/releases/<sha>/   сборка коммита
#   <корень>/current  → releases/<sha>    её отдаёт nginx (root …/current)
#   <корень>/previous → releases/<прежний sha>   бандлы для вкладок со старым index.html
#
#   sh IaC/publish-static.sh <каталог сборки> <корень> <sha>
#
# Ссылки переключаются rename-ом поверх старых: nginx в любой момент видит целую сборку,
# а не полупустой каталог, — ни 404, ни ChunkLoadError посреди выкатки. Вкладка, открытая
# до выкатки, догружает ленивые чанки своей версии из previous (try_files в nginx.conf).
# Ссылки относительные: корень смонтирован в контейнер nginx под другим путём.
# Через `sh`, а не ./: `dotnet new` не сохраняет бит исполнения (спец. infrastructure, 13).
set -eu

build=${1:?"использование: sh IaC/publish-static.sh <каталог сборки> <корень> <sha>"}
root=${2:?корень}
sha=${3:?sha}
keep=${KEEP_RELEASES:-3}

[ -f "$build/index.html" ] || { echo "::error::в $build нет index.html — сборки нет"; exit 1; }

mkdir -p "$root/releases"
release="$root/releases/$sha"
current=$(readlink "$root/current" 2>/dev/null || true)

if [ "$current" = "releases/$sha" ]; then
  echo "releases/$sha уже выкачен — ничего не меняю"
else
  # Каталог собирается рядом и переезжает целиком. `/.` а не `/*`: звёздочка не берёт
  # скрытые файлы сборки
  if [ ! -d "$release" ]; then
    rm -rf "$release.tmp"
    mkdir "$release.tmp"
    cp -r "$build/." "$release.tmp/"
    mv "$release.tmp" "$release"
  fi
  # previous — раньше current: в момент переключения старые чанки уже доступны
  if [ -n "$current" ]; then
    ln -sfn "$current" "$root/previous.tmp"
    mv -Tf "$root/previous.tmp" "$root/previous"
  fi
  ln -sfn "releases/$sha" "$root/current.tmp"
  mv -Tf "$root/current.tmp" "$root/current"
  echo "current → releases/$sha${current:+, previous → $current}"
fi
# Свежесть выпуска — по времени выкатки, а не копирования: по нему чистится хвост
touch "$release"

# Хвост: последние $keep по времени; current и previous не удаляются никогда.
# ls, а не глоб: нужен порядок по времени; имена — sha, без пробелов
current=$(readlink "$root/current" | sed 's|^releases/||')
previous=$(readlink "$root/previous" 2>/dev/null | sed 's|^releases/||' || true)
# shellcheck disable=SC2010
ls -1t "$root/releases" | grep -v '\.tmp$' | tail -n +$((keep + 1)) | while read -r old; do
  if [ "$old" != "$current" ] && [ "$old" != "$previous" ]; then
    rm -rf "${root:?}/releases/$old"
    echo "удалён выпуск $old"
  fi
done

echo "Откат — на прежний выпуск, без пересборки:"
echo "  ln -sfn releases/<sha> $root/current.tmp && mv -Tf $root/current.tmp $root/current"
ls -lt "$root/releases"
