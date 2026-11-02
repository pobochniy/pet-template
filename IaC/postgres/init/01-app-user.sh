#!/bin/bash
# Пользователь приложения (спец. infrastructure, 8 и 13; IaC/DEPLOYMENT.md, «Пользователь
# приложения») — аналог `user_write` в Lad/ASU: API ходит в базу не суперпользователем
# `postgres`, а ролью без прав за пределами своей базы.
#
# Роль — владелец базы `POSTGRES_DB`: миграции накатывает сам API при старте
# (`Program.cs`, `MigrateAsync`), значит ему нужны CREATE/ALTER/DROP в своей схеме
# и `CREATE EXTENSION citext` (расширение доверенное — владельцу базы суперпользователь
# не нужен). Схема `public` в Postgres 15+ принадлежит `pg_database_owner` — то есть
# тому, кто владеет базой, отдельный GRANT на неё не нужен.
# Чего у роли нет: SUPERUSER, CREATEDB, CREATEROLE, REPLICATION, BYPASSRLS — она не
# создаст базу или роль, не прочтёт чужую базу, не включит недоверенное расширение
# и не дотянется до файлов сервера (`COPY ... TO PROGRAM`, `pg_read_file`).
# Разделить на владельца для миграций и роль только с DML — tech-debt/db-app-role.md.
#
# Запускается образом postgres сам — один раз, на пустом томе `db-data`. Скрипт
# идемпотентный: на живой базе (переезд, смена пароля) его можно запустить руками,
# он создаст недостающее и выставит пароль из `.env`:
#   docker compose exec database /docker-entrypoint-initdb.d/01-app-user.sh
# (переменные APP_DB_* контейнер берёт из `.env` при создании — после правки `.env`
# сначала `docker compose up -d database`, он пересоздаст контейнер с новыми значениями).
set -euo pipefail

: "${APP_DB_USER:?APP_DB_USER не задан — см. IaC/.env.example}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD не задан — см. IaC/.env.example}"

# Подключение — по unix-сокету внутри контейнера, суперпользователем: там доверенный
# вход (`local all all trust` у образа postgres), пароль не нужен. Значения уходят
# переменными psql и подставляются через format(%I, %L) — кавычки и спецсимволы
# в пароле не ломают SQL
psql -v ON_ERROR_STOP=1 --no-psqlrc \
    --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -v app_user="$APP_DB_USER" -v app_password="$APP_DB_PASSWORD" -v db="$POSTGRES_DB" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN', :'app_user')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'app_user')
\gexec

SELECT format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
              :'app_user', :'app_password')
\gexec

SELECT format('ALTER DATABASE %I OWNER TO %I', :'db', :'app_user')
\gexec
SQL

echo "01-app-user.sh: роль $APP_DB_USER — владелец базы $POSTGRES_DB"
