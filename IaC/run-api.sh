#!/bin/sh
# Поднять контейнер API (dapi) из образа и дождаться /health (спец. infrastructure, 6).
# Один docker run на publish-api.yml и rollback-api.yml: новый секрет — строкой ниже
# и строкой в env: шага обоих workflow (спец. infrastructure, 9).
#
#   sh IaC/run-api.sh <тег | sha256:id образа>
#
# Через `sh`, а не ./: `dotnet new` не сохраняет бит исполнения (спец. infrastructure, 13).
# Секреты — переменными окружения шага: `--env ИМЯ` без значения docker берёт из своего
# окружения, и в списке процессов (`ps`) их не видно.
# Код выхода: 0 — контейнер ответил 200 на /health; 1 — нет за HEALTH_TIMEOUT секунд
# или контейнер упал (лог — в вывод). Что делать с упавшим — решает workflow.
set -eu

image_ref=${1:?"использование: sh IaC/run-api.sh <тег | sha256:id>"}
case "$image_ref" in
  sha256:*) image="$image_ref" ;;
  *) image="pettemplate-api:$image_ref" ;;
esac
timeout=${HEALTH_TIMEOUT:-120}

# Справка (спец. help, 6.4): весь ~/pettemplate-help только на чтение — ссылка current
# в нём относительная и ведёт в releases/ рядом. Каталог заводим сами: иначе докер
# создаст его от root, и публикация справки не сможет в него писать
mkdir -p "$HOME/pettemplate-help"

docker rm -f dapi >/dev/null 2>&1 || true

# Порт — только на 127.0.0.1: снаружи API достижим лишь через nginx, иначе замок админки
# по адресу (спец. admin, 7) обходится прямым заходом на порт, а X-Forwarded-For, по
# которому лимит входа считает адреса, подделывается.
# Адреса соседних контейнеров — не секреты: appsettings.Release.json. Исключение —
# OTEL_* (спец. infrastructure, 10): их по стандартным именам читает сам экспортёр OTLP.
# Память — потолок, а не резерв: машина в 6 ГБ делится с базой, Qdrant и телеметрией
# (IaC/docker-compose.yml); GC .NET видит лимит контейнера и подстраивается под него
docker run --restart unless-stopped -d --name dapi --network nginx-proxy-man \
  -p 127.0.0.1:5000:8080 \
  --memory 1536m \
  --env ASPNETCORE_ENVIRONMENT=Release \
  --env ConnectionStrings__AppConnection \
  --env JwtOptions__SigningKey \
  --env AdminOptions__SigningKey \
  --env Telegram__BotToken \
  --env TelegramLoginOptions__BotId \
  --env VkIdOptions__AppId \
  --env VkOptions__PrivateKey \
  --env MailOptions__Password \
  --env AssistantOptions__Providers__main__Endpoint \
  --env AssistantOptions__Providers__main__ApiKey \
  --env AssistantOptions__SeedModels__0__Key \
  --env AssistantOptions__Transcription__Model \
  --env OTEL_EXPORTER_OTLP_ENDPOINT=http://otelcollectorcontainer:4317 \
  --env OTEL_SERVICE_NAME=pettemplate-api \
  -v "$HOME/pettemplate-help:/app/help:ro" \
  "$image" >/dev/null

echo "dapi ← $image, жду /health до ${timeout} с"

# Спрашиваем сами, а не ждём статуса HEALTHCHECK образа: у того первая проверка через
# interval, а здесь важно узнать сразу. Контейнер падает на старте раз за разом (нет
# Qdrant, короткий ключ подписи) — docker перезапускает его сам, ждать дальше незачем
elapsed=0
while [ "$elapsed" -lt "$timeout" ]; do
  if curl -fsS -m 5 -o /dev/null http://127.0.0.1:5000/health 2>/dev/null; then
    echo "dapi здоров через ${elapsed} с"
    exit 0
  fi
  if [ "$(docker inspect -f '{{.RestartCount}}' dapi 2>/dev/null || echo 99)" -ge 3 ]; then
    echo "::error::dapi падает на старте"
    break
  fi
  sleep 2
  elapsed=$((elapsed + 2))
done

[ "$elapsed" -ge "$timeout" ] && echo "::error::dapi не ответил 200 на /health за ${timeout} с"
curl -sS -m 5 http://127.0.0.1:5000/health 2>&1 || true
echo
docker logs --tail 100 dapi 2>&1 || true
exit 1
