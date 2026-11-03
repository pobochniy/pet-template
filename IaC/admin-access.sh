#!/bin/bash
# Открыть и закрыть adminka.example.com (и grafana.example.com за тем же замком) — спец. admin, 7.
#
#   admin-access.sh open <часы>   открыть всем на 1, 2, 4 или 8 часов (повторный — продлевает)
#   admin-access.sh close         закрыть
#   admin-access.sh expire        закрыть, если срок вышел; без открытия — убедиться, что nginx закрыт
#
# Зовут двое: workflow `Admin Access` (ручные open/close и expire по расписанию раннера)
# и cron на самой VM (expire раз в 5 минут, ставит setup-vm.sh): срок окна не держится
# на одном раннере — лёг он или GitHub отложил расписание, окно закроет VM сама
# (tech-debt/admin-perimeter.md). Запуски не пересекаются: их разводит flock ниже.
#
# Запуск — `bash admin-access.sh …`: `dotnet new` не сохраняет бит исполнения
# (спец. infrastructure, 13). Кто открыл — ACTOR (workflow передаёт github.actor).
# Сводка в GitHub — GITHUB_STEP_SUMMARY; вне Actions она уходит в никуда

ACTION="${1:-}"
HOURS="${2:-}"
ACTOR="${ACTOR:-$(whoami)}"
GITHUB_STEP_SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/null}"
# cron запускает с урезанным PATH — docker, curl и flock должны находиться и там
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:$PATH"

set -euo pipefail

# nginx из IaC/docker-compose.yml — сервис `webserver`, имени контейнера у него нет
nginx=$(docker ps -q --filter label=com.docker.compose.service=webserver)
if [ "$(echo "$nginx" | grep -c .)" != "1" ]; then
  echo "::error::ожидался ровно один запущенный контейнер nginx (сервис webserver), найдено: $(echo "$nginx" | grep -c .)"
  exit 1
fi
conf=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/etc/nginx/conf.d"}}{{.Source}}{{end}}{{end}}' "$nginx")
if [ -z "$conf" ] || [ ! -d "$conf" ]; then
  echo "::error::не нашёл на хосте каталог, смонтированный в /etc/nginx/conf.d"
  exit 1
fi
file="$conf/admin-allow-open.inc"

# Один запуск за раз (зачем flock, а не concurrency, — над permissions). Замок
# в каталоге конфигов: он один на хост, сколько бы раннеров там ни жило.
# Маску admin-allow*.inc имя не задевает
exec 9>"$conf/.admin-access.lock"
if ! flock -w 300 9; then
  echo "::error::другой запуск Admin Access держит замок дольше 5 минут"
  exit 1
fi

# Проверка и reload через return, а не двумя строками: внутри `if !` set -e не
# действует, а reload с битым конфигом ответит 0 и оставит старый. Вывод
# `nginx -t` остаётся в $nginx_t — показать, какой файл сломан
nginx_t=""
reload() {
  if ! nginx_t=$(docker exec "$nginx" nginx -t 2>&1); then
    return 1
  fi
  docker exec "$nginx" nginx -s reload
}

# Почему не прошёл reload — в лог, в сводку запуска и в саму ошибку
# (перевод строки в `::error::` — %0A). Конфиг ломает и чужой файл в conf.d,
# не только наш: тогда никакой reload не пройдёт, пока его не поправят
report_reload() {
  local text="$1"
  echo "$nginx_t"
  {
    echo "### ❗ $text"
    echo '```'
    echo "$nginx_t"
    echo '```'
  } >> "$GITHUB_STEP_SUMMARY"
  local details="${nginx_t//%/%25}"
  details="${details//$'\r'/}"
  details="${details//$'\n'/%0A}"
  echo "::error::$text%0A%0Anginx -t:%0A$details"
}

# Что nginx отвечает на самом деле, а не что лежит на диске: упавший reload
# оставляет в памяти старый конфиг с `allow all`, хотя файла уже нет. Стучимся
# с самого хоста в опубликованный порт 443 — для nginx это адрес шлюза docker,
# его постоянный admin-allow.inc не пускает, и закрытая админка отвечает 403.
# Нет ответа — 000
port=$(docker port "$nginx" 443/tcp | head -n 1 | sed 's/.*://')
if [ -z "$port" ]; then
  echo "::error::у контейнера nginx не опубликован порт 443 — проверить, закрыта ли админка, нечем"
  exit 1
fi
status() {
  curl -sk -m 10 -o /dev/null -w '%{http_code}' \
    --resolve "adminka.example.com:$port:127.0.0.1" "https://adminka.example.com:$port/" || true
}

close() {
  rm -f "$file"
  if ! reload; then
    # Файла нет, а nginx живёт со старым конфигом — открыто. Следующий запуск
    # по расписанию увидит это по ответу и повторит, но пройдёт он, только когда
    # конфиг поправят руками (tech-debt/admin-perimeter.md)
    report_reload "nginx не перезагрузился — админка остаётся открытой, пока это не исправят. Что делать на VM: по выводу nginx -t ниже поправить или убрать сломанный файл в $conf, затем Run workflow с close (или дождаться запуска по расписанию). Если nginx -t уже проходит, а reload всё равно не идёт, — docker restart $nginx. С непроходящим nginx -t контейнер не перезапускать: nginx не поднимется и ляжет весь сайт"
    exit 1
  fi
  # reload не ждёт новых воркеров — даём им подняться
  code=""
  for _ in 1 2 3 4 5 6 7 8 9 10; do
    code=$(status)
    [ "$code" = "403" ] && break
    sleep 1
  done
  if [ "$code" != "403" ]; then
    echo "::error::после закрытия adminka отвечает $code, а не 403 — закрытие повторит запуск по расписанию"
    exit 1
  fi
  echo "🔒 Админка закрыта" | tee -a "$GITHUB_STEP_SUMMARY"
}

case "$ACTION" in
  open)
    case "$HOURS" in
      1|2|4|8) ;;
      *) echo "::error::hours — 1, 2, 4 или 8"; exit 1 ;;
    esac
    until=$(( $(date -u +%s) + HOURS * 3600 ))
    until_text=$(date -u -d "@$until" '+%Y-%m-%d %H:%M UTC')
    # Повторный open продлевает срок: файл переписывается целиком. Прежний
    # сохраняется — откат должен вернуть его, а не стереть: nginx живёт с ним,
    # и без файла открытое осталось бы без срока
    rm -f "$file.prev"
    [ -f "$file" ] && cp -p "$file" "$file.prev"
    printf '# Открыл %s через Admin Access; закроется не раньше %s\n# until=%s\nallow all;\n' \
      "$ACTOR" "$until_text" "$until" > "$file.part"
    mv "$file.part" "$file"
    # Конфиг не прошёл проверку — открытие откатывается, а не висит до reload
    if ! reload; then
      if [ -f "$file.prev" ]; then
        mv "$file.prev" "$file"
        report_reload "nginx -t не прошёл, продление отменено — срок прежний. Сломанный файл — по выводу ниже; пока его не поправят, close по сроку тоже не пройдёт"
      else
        rm -f "$file"
        report_reload "nginx -t не прошёл, открытие отменено. Сломанный файл — по выводу ниже, в $conf"
      fi
      exit 1
    fi
    rm -f "$file.prev"
    echo "🔓 Админка открыта до $until_text — закрыть: Run workflow с close" | tee -a "$GITHUB_STEP_SUMMARY"
    ;;
  close)
    close
    ;;
  expire)
    if [ ! -f "$file" ]; then
      code=$(status)
      if [ "$code" = "403" ]; then
        echo "закрыто, делать нечего"
        exit 0
      fi
      # Файла нет, а пускает: прошлое закрытие не дошло до nginx
      echo "::warning::файла нет, а adminka отвечает $code — повторяю закрытие"
      close
      exit 0
    fi
    until=$(sed -n 's/^# until=\([0-9][0-9]*\)$/\1/p' "$file")
    # Срока в файле нет — файл не наш или битый; держать открытым без срока нельзя
    if [ -z "$until" ] || [ "$(date -u +%s)" -ge "$until" ]; then
      close
    else
      echo "открыто ещё $(( (until - $(date -u +%s)) / 60 )) мин"
    fi
    ;;
  *)
    echo "::error::неизвестное действие: $ACTION"
    exit 1
    ;;
esac
