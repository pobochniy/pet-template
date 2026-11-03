# PetTemplate.Db — база и миграции

Правила работы с сущностями и DTO — в [`agents.md`](agents.md). База — PostgreSQL 18,
имена таблиц и колонок в ней — snake_case, логин и почта — `citext`
(`specs/infrastructure.md`, 3.1).

## Подключение к базе

Строка подключения везде одна и та же настройка — `ConnectionStrings:AppConnection`.
Различаются хост и порт, и это главный источник путаницы: `localhost:5432` верен ровно
для одного из трёх сценариев.

### Локальная разработка

База поднимается корневым `docker-compose.yml`, значение уже лежит в
`appsettings.Development.json`, делать ничего не нужно:

```
Host=localhost;Port=5432;Database=pettemplatedb;Username=postgres;Password=1234
```

```bash
docker compose up -d              # из корня репозитория
docker compose exec postgres psql -U postgres -d pettemplatedb   # посмотреть руками
cd src/PetTemplate.Api && dotnet run
```

Порт `5432` стандартный: если на машине уже есть свой Postgres, поменяйте публикацию
в `docker-compose.yml` и порт в строке вместе. В `Development` API миграции сам не накатывает —
схему в пустую дев-базу поднимает `dotnet ef database update` (ниже, «Миграции»).

### Прод, изнутри докера

Так ходит контейнер API (`dapi`) на боевой VM:

```
Host=dbcontainer;Port=5432;Database=pettemplatedb;Username=<пользователь приложения>;Password=<пароль>
```

- `dbcontainer` — имя контейнера Postgres (`container_name` в `IaC/docker-compose.yml`).
  Его резолвит встроенный DNS сети `nginx-proxy-man`, в которой лежат оба контейнера:
  база — через compose, `dapi` — через `--network nginx-proxy-man` в `publish-api.yml`.
- Порт **внутренний, 5432**; `localhost` изнутри контейнера ведёт в сам контейнер API.
- Пользователь — не суперпользователь `postgres`, а пользователь приложения `pettemplate_app`
  (`APP_DB_USER` в `IaC/.env`) — только DML: схему он не меняет. Миграции на проде накатывает
  бандл на шаге выкатки под владельцем базы `pettemplate_owner` (`APP_DB_OWNER`). Обе роли
  заводит `IaC/postgres/init/01-app-user.sh` при первом старте тома (`IaC/DEPLOYMENT.md`, раздел 9).

### Прод, с локальной машины

Порт базы на хост не опубликован вовсе. Руками — на VM:
`docker exec -it dbcontainer psql -U postgres -d pettemplatedb`. Из DBeaver/DataGrip — через
SSH-туннель: сначала раскомментировать публикацию `127.0.0.1:${DB_PORT:-5432}:5432` у `database`
в `IaC/docker-compose.yml` (только на `127.0.0.1` — `IaC/README.md`, «Доступ к БД»), потом
`ssh -N -L 15432:127.0.0.1:5432 user@<ip-вм>`. Локальный конец туннеля берите не `5432`: там уже
слушает дев-база, и обе называются `pettemplatedb` — перепутать легко. Проверка, что вы
на проде: `select inet_server_addr(), current_database();`.

Ручной SQL — без кавычек, snake_case: `select user_name, email from profiles;`.

## Миграции

Команды — из корня репозитория (`dotnet tool restore` поставит `dotnet-ef` той же версии,
что в выкатке, — манифест `dotnet-tools.json`). Контекст `dotnet ef` создаёт
`Entity/ApplicationContextFactory`, без хоста API: строка — из `ConnectionStrings__AppConnection`
в окружении, а без неё — дев-база из корневого `docker-compose.yml`.

```bash
# новая миграция
dotnet ef migrations add <Имя> --startup-project src/PetTemplate.Api --project src/PetTemplate.Db

# накатить на локальную базу (до последней или до указанной)
dotnet ef database update --startup-project src/PetTemplate.Api --project src/PetTemplate.Db
dotnet ef database update <Имя> --startup-project src/PetTemplate.Api --project src/PetTemplate.Db

# убрать последнюю, ещё не накаченную миграцию
dotnet ef migrations remove --startup-project src/PetTemplate.Api --project src/PetTemplate.Db

# SQL-скрипт миграций — посмотреть, что уйдёт в базу
dotnet ef migrations script --startup-project src/PetTemplate.Api --project src/PetTemplate.Db

# бандл — то, что накатывает выкатка (publish-api.yml, шаг apply migrations)
dotnet ef migrations bundle --startup-project src/PetTemplate.Api --project src/PetTemplate.Db --output efbundle --force
```

### Первая миграция `Init`

`Init` собрана под PostgreSQL (Npgsql) и создаёт всю схему с нуля. Что в ней проверять
глазами при пересборке (в `Migrations/*_Init.cs` и в SQL `migrations script`):

- расширение `citext` — `CREATE EXTENSION IF NOT EXISTS citext` **до** первой таблицы;
  `user_name` и `email` у `profiles` — `citext`;
- уникальный индекс фактов памяти ассистента `assistant_facts` по
  `(workspace_id, user_id, kind, key)` — `NULLS NOT DISTINCT` (`AssistantFact.OwnerKeyIndex`);
- частичный индекс `WHERE status = 0` у `admin_jobs` (`HasFilter`); у `assistant_runs` индекс
  «один живой ход на тред» в `Init` тоже `status = 0`, а с `AssistantLiveRunAndSpendCaps` —
  `finished_at_utc IS NULL`;
- у `ai_models` нет колонки `version`/`xmin`: `xmin` — системная колонка Postgres;
- имена таблиц, колонок и индексов — snake_case (`ix_…`, `pk_…`): по ним `DbErrors`
  разбирает нарушения уникальности.

Тесты раскатывают схему этой же миграцией (`MigrateAsync` в
`tests/PetTemplate.Tests/Containers/PostgresServer.cs`), так что сломанную миграцию ловит прогон.
