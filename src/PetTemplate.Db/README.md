# PetTemplate.Db — база и миграции

Правила работы с сущностями и DTO — в [`agents.md`](agents.md). База — PostgreSQL 17,
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
в `docker-compose.yml` и порт в строке вместе. Схемы в пустой базе пока не будет: старая
миграция `Init` собрана под MySQL и исключена из сборки, пока её не пересобрали под
PostgreSQL (ниже, «Первая миграция `Init`»); до тех пор схему поднимают только тесты
через `EnsureCreated`.

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
  (`APP_DB_USER` в `IaC/.env`) — владелец своей базы без прав суперпользователя. Заводит его
  `IaC/postgres/init/01-app-user.sh` при первом старте тома (`IaC/DEPLOYMENT.md`, раздел 9).

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

Команды — из корня репозитория: модель лежит здесь, а строку подключения и конфигурацию
EF берёт стартовый проект API.

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
```

### Первая миграция `Init`

В `Migrations/` лежит `Init` старого шаблона под MySQL (Pomelo): без него она не компилируется
и исключена из сборки (`<Compile Remove="Migrations/**" />` в `PetTemplate.Db.csproj`).
Пока её не пересобрали, вне `Development` API зовёт `MigrateAsync()` впустую и на пустой
базе не стартует, а тесты поднимают схему `EnsureCreated` — сломанную миграцию они не ловят.
Пересборка — один раз, владельцем шаблона (или первым коммитом проекта, если шаблон
дошёл до него без неё):

1. Удалить старую миграцию целиком: `rm -rf src/PetTemplate.Db/Migrations`.
2. В `src/PetTemplate.Db/PetTemplate.Db.csproj` убрать блок с комментарием про Pomelo —
   `<ItemGroup>` с `<Compile Remove="Migrations/**" />` и `<None Include="Migrations/**" />`.
3. Собрать новую из корня репозитория (база для этого не нужна — `migrations add` к ней
   не ходит; `dotnet-ef` — глобальный инструмент, `dotnet tool update -g dotnet-ef`):

   ```bash
   dotnet ef migrations add Init --startup-project src/PetTemplate.Api --project src/PetTemplate.Db
   ```

4. Глазами — в `Migrations/*_Init.cs` и в SQL (`dotnet ef migrations script --startup-project
   src/PetTemplate.Api --project src/PetTemplate.Db`):
   - расширение `citext` — `AlterDatabase().Annotation("Npgsql:PostgresExtension:citext", …)`,
     в SQL `CREATE EXTENSION IF NOT EXISTS citext` **до** первой таблицы; `user_name` и `email`
     у `profiles` — `citext`;
   - уникальный индекс фактов памяти ассистента `assistant_facts` по
     `(workspace_id, user_id, kind, key)` — с `Npgsql:NullsDistinct = false`, в SQL
     `NULLS NOT DISTINCT` (`AssistantFact.OwnerKeyIndex`);
   - частичные индексы `WHERE status = 0` у `admin_jobs` и `assistant_runs` (`HasFilter`);
   - у `ai_models` нет колонки `version`/`xmin`: `xmin` — системная колонка Postgres,
     миграция её не создаёт;
   - имена таблиц, колонок и индексов — snake_case (`ix_…`, `pk_…`): по ним `DbErrors`
     разбирает нарушения уникальности.
5. Тесты — со схемой из миграции, а не из модели:
   - `tests/PetTemplate.Tests/Containers/PostgresServer.cs`, `CreateTemplateAsync` —
     `EnsureCreatedAsync()` → `MigrateAsync()` (и комментарий над методом);
   - `tests/PetTemplate.Tests/SetupApplication.cs` — убрать `db.Database.EnsureCreated()`
     с комментарием: на базе с историей миграций он молча ничего не делает;
   - `ApiApplicationFactory` оставить в `Development` (старт API не зовёт `MigrateAsync()`,
     схема уже в копии шаблонной базы), поправить в нём комментарий про отсутствие миграций;
   - тесты поиска менять не нужно: `TestNotesContext.CreateTable` зовёт
     `IRelationalDatabaseCreator.CreateTablesAsync` — создаёт по своей модели только
     `test_notes` и на историю миграций не смотрит, поэтому на мигрированной базе работает
     так же, как на созданной `EnsureCreated`. Таблица остаётся вне миграции и вне шаблонной базы.
6. `dotnet build PetTemplate.slnx` и `dotnet test PetTemplate.slnx` — зелёные; локально
   `dotnet ef database update --startup-project src/PetTemplate.Api --project src/PetTemplate.Db`
   на пустую дев-базу и `dotnet run` API — стартует.
7. Убрать упоминания «до пересборки `Init`»: этот раздел и абзац о пустой схеме выше,
   `tests/PetTemplate.Tests/agents.md` («Схема раскатывается один раз»), `AiModelSeeder`,
   `specs/infrastructure.md` (3.1 и 13, «Не реализовано», пункт 1).
