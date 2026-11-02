#!/bin/bash
# Пользователь postgres-exporter (спец. infrastructure, 10; IaC/DEPLOYMENT.md, «Метрики
# PostgreSQL и машины») — аналог `exporter` у mysqld-exporter в Lad/ASU: только служебная
# статистика, к данным доступа нет.
#
# Права — встроенная роль `pg_monitor`: читает pg_stat_* и pg_settings целиком (в том числе
# чужие запросы в pg_stat_activity) и размеры объектов, но не строки таблиц. Своих таблиц
# у роли нет, CONNECT к базам у неё — как у всех (PUBLIC). Соединений не больше трёх:
# экспортёр держит одно, и съесть max_connections у API он не сможет.
#
# Запускается образом postgres сам — один раз, на пустом томе `db-data`, после
# 01-app-user.sh. Скрипт идемпотентный: на живой базе (том заведён до экспортёра, смена
# пароля) его запускают руками:
#   docker compose exec database /docker-entrypoint-initdb.d/02-exporter-user.sh
# (переменные POSTGRES_EXPORTER_* контейнер берёт из `.env` при создании — после правки
# `.env` сначала `docker compose up -d database`, он пересоздаст контейнер с новыми значениями).
#
# Пароль не задан — пользователь не заводится, а база всё равно встаёт: метрики базы
# не повод ронять первичную инициализацию. Экспортёр тогда отвечает pg_up = 0
set -euo pipefail

if [[ -z "${POSTGRES_EXPORTER_PASSWORD:-}" ]]; then
    echo "02-exporter-user.sh: POSTGRES_EXPORTER_PASSWORD не задан — пользователь экспортёра не заведён (IaC/.env.example)"
    exit 0
fi

EXPORTER_USER="${POSTGRES_EXPORTER_USER:-postgres_exporter}"

# Подключение — как в 01-app-user.sh: по unix-сокету суперпользователем, значения —
# переменными psql через format(%I, %L)
psql -v ON_ERROR_STOP=1 --no-psqlrc \
    --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -v exporter_user="$EXPORTER_USER" -v exporter_password="$POSTGRES_EXPORTER_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN', :'exporter_user')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'exporter_user')
\gexec

SELECT format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 3 PASSWORD %L',
              :'exporter_user', :'exporter_password')
\gexec

SELECT format('GRANT pg_monitor TO %I', :'exporter_user')
\gexec
SQL

echo "02-exporter-user.sh: роль $EXPORTER_USER с pg_monitor"
