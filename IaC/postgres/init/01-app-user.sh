#!/bin/bash
# Роли базы (спец. infrastructure, 8 и 13; IaC/DEPLOYMENT.md, «Пользователь приложения»):
# ни миграции, ни API не ходят суперпользователем `postgres`, и у API нет прав на схему.
#
# - `APP_DB_OWNER` (`pettemplate_owner`) — владелец базы и всех её объектов. Под ним
#   накатывает миграции бандл в `publish-api.yml` (секрет MIGRATIONS_DB_CONNECTION_STRING):
#   CREATE/ALTER/DROP в своей схеме и `CREATE EXTENSION citext` (расширение доверенное —
#   владельцу базы суперпользователь не нужен; схема `public` в Postgres 15+ принадлежит
#   `pg_database_owner`, то есть ему).
# - `APP_DB_USER` (`pettemplate_app`) — под ним ходит API (API_DB_CONNECTION_STRING): только
#   SELECT/INSERT/UPDATE/DELETE на таблицы и USAGE/SELECT на последовательности. Таблицы
#   будущих миграций достаются ему сами — `ALTER DEFAULT PRIVILEGES FOR ROLE` владельца.
#   DROP, ALTER, TRUNCATE и CREATE в схеме ему недоступны: SQL-инъекция или ошибка в коде
#   портит строки, но не схему.
# Ни у одной нет SUPERUSER, CREATEDB, CREATEROLE, REPLICATION, BYPASSRLS — не создадут
# базу или роль, не прочтут чужую базу и не дотянутся до файлов сервера
# (`COPY ... TO PROGRAM`, `pg_read_file`).
#
# Запускается образом postgres сам — один раз, на пустом томе `db-data`. Скрипт
# идемпотентный: на живой базе (переезд, смена пароля, база шаблона v1, где владельцем
# был сам `APP_DB_USER`) его запускают руками — он создаст недостающее, передаст
# объекты владельцу и выставит пароли из `.env`:
#   docker compose exec database /docker-entrypoint-initdb.d/01-app-user.sh
# (переменные APP_DB_* контейнер берёт из `.env` при создании — после правки `.env`
# сначала `docker compose up -d database`, он пересоздаст контейнер с новыми значениями).
set -euo pipefail

: "${APP_DB_USER:?APP_DB_USER не задан — см. IaC/.env.example}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD не задан — см. IaC/.env.example}"
: "${APP_DB_OWNER:?APP_DB_OWNER не задан — см. IaC/.env.example}"
: "${APP_DB_OWNER_PASSWORD:?APP_DB_OWNER_PASSWORD не задан — см. IaC/.env.example}"

# Подключение — по unix-сокету внутри контейнера, суперпользователем: там доверенный
# вход (`local all all trust` у образа postgres), пароль не нужен. Значения уходят
# переменными psql и подставляются через format(%I, %L) — кавычки и спецсимволы
# в пароле не ломают SQL. Порядок важен: сначала владелец и передача ему объектов,
# потом права роли API — на уже переданные таблицы и на будущие
psql -v ON_ERROR_STOP=1 --no-psqlrc \
    --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -v owner="$APP_DB_OWNER" -v owner_password="$APP_DB_OWNER_PASSWORD" \
    -v app_user="$APP_DB_USER" -v app_password="$APP_DB_PASSWORD" -v db="$POSTGRES_DB" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN', :'owner')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'owner')
\gexec

SELECT format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
              :'owner', :'owner_password')
\gexec

SELECT format('CREATE ROLE %I LOGIN', :'app_user')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'app_user')
\gexec

SELECT format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
              :'app_user', :'app_password')
\gexec

SELECT format('ALTER DATABASE %I OWNER TO %I', :'db', :'owner')
\gexec

-- База шаблона v1: владельцем таблиц была роль API, их миграции заводил сам API
SELECT format('REASSIGN OWNED BY %I TO %I', :'app_user', :'owner')
\gexec

SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'db', :'app_user')
\gexec

SELECT format('GRANT USAGE ON SCHEMA public TO %I', :'app_user')
\gexec

SELECT format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO %I', :'app_user')
\gexec

SELECT format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO %I', :'app_user')
\gexec

SELECT format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I',
              :'owner', :'app_user')
\gexec

SELECT format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO %I',
              :'owner', :'app_user')
\gexec
SQL

echo "01-app-user.sh: $APP_DB_OWNER — владелец базы $POSTGRES_DB, $APP_DB_USER — только DML"
