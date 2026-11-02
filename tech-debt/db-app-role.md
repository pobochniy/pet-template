# Роль API в базе — владелец, а не только DML

API ходит в Postgres ролью `pettemplate_app` (`APP_DB_USER`), и эта роль — владелец базы
`pettemplatedb` (`IaC/postgres/init/01-app-user.sh`; `specs/infrastructure.md`, 8;
`IaC/DEPLOYMENT.md`, «Пользователь приложения»). Это ровно то, что было у `user_write`
в Lad/ASU (`ALL` на свою базу), но не минимально нужное приложению.

Почему так: миграции накатывает сам API при старте (`Program.cs`, `MigrateAsync`) одной и той же
строкой подключения. Роли только с `SELECT/INSERT/UPDATE/DELETE` миграции не пройдут —
нужны `CREATE/ALTER/DROP` в схеме и `create extension citext`.

Чем плохо: уязвимость в API с SQL-инъекцией или ошибка в коде может не только читать и
портить строки, но и `drop table`, `alter table`, `truncate` — всё, что умеет владелец.
Суперпользователем роль при этом не является: файлы сервера, другие базы и роли ей недоступны.

## Что сделать

- Две роли: `pettemplate_owner` (`NOLOGIN` или с паролем только для миграций) — владелец базы
  и схемы; `pettemplate_app` — `CONNECT`, `USAGE` на схему, DML на все таблицы и `USAGE,
  SELECT` на последовательности, плюс `ALTER DEFAULT PRIVILEGES FOR ROLE pettemplate_owner
  IN SCHEMA public GRANT ... TO pettemplate_app` — чтобы новые таблицы из миграций сразу
  были доступны.
- Миграции — отдельной строкой подключения под владельцем: либо вторая строка
  (`ConnectionStrings:MigrationConnection`) только для `MigrateAsync` на старте, либо
  отдельный шаг в `publish-api.yml` (`dotnet ef migrations bundle` и запуск бандла под
  владельцем до `docker run` API). Второй вариант заодно убирает гонку при нескольких
  экземплярах API.
- Бэкап и восстановление не меняются: владельцем объектов в дампе станет `pettemplate_owner`.

Делать, когда у проекта появятся данные, которые жалко, или требования аудита к правам.
