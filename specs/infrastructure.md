# Спецификация: репозиторий, проверки и выкатка

Как код проекта из шаблона PetTemplate доезжает до прода: устройство репозитория, ворота до коммита
и до выкатки, workflow GitHub Actions, инфраструктура виртуальной машины (`IaC/`). Решения шаблона — раздел 12.

> **Статус.** Описывает то, что лежит в репозитории шаблона. Чего нет или что сделано не до
> конца — раздел 13. Своего прода у шаблона нет: «прод» здесь — сервер проекта, созданного
> через `dotnet new`.

---

## 1. Зачем и принципы

Шаблон отдаёт проекту не только код, но и путь до прода: хуки, ворота выкатки, workflow
и описание одной виртуальной машины, на которой живут API, клиент, база, поиск и телеметрия.
Проект из шаблона в первый же день выкатывается готовым способом и не изобретает его заново.

- **Красная ветка до прода не доезжает.** Проверки стоят в самих workflow выкатки,
  а не в отдельном «CI», на который можно не посмотреть: упали тесты — образ не собирается,
  контейнер не трогается.
- **То же самое — раньше.** Хуки `pre-commit` и `pre-push` гоняют те же проверки, что ворота
  выкатки, но до пуша и только по тому, чего коснулись. В хуках — только то, что укладывается
  в секунды.
- **Секретов в репозитории нет.** `appsettings*.json` — образец структуры и значения для
  разработки; боевое приезжает в контейнер переменными окружения из секретов GitHub при выкатке.
- **Одна виртуалка, self-hosted раннер на ней.** Деплой — `docker build` и `docker run` на самой
  машине, без реестра образов. Что нужно от хоста (Chrome для karma) — на облачном раннере.
- **Выкатка — руками, `Run workflow`.** Не на push в `main`: выкатывается осознанно та ветка,
  которую выкатывают, и видно, кто и когда. Workflow с push-триггером, который сразу деплоит,
  в шаблоне нет.
- **Откат — такой же workflow.** Образы API хранятся по дате сборки, предыдущая сборка фронта —
  в соседнем каталоге.

---

## 2. Репозиторий

### 2.1 Решение — `PetTemplate.slnx`

Решение — `PetTemplate.slnx`, XML-формат, который читают Rider, VS 2022 17.14+ и `dotnet` SDK
от 9.0.200. Причины: читаемый diff, нет GUID и секций конфигураций, проекты группируются папками
решения без правки сорока строк. Прежний `PetTemplate.sln` удалён — два файла решения
разъезжаются первыми.

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/PetTemplate.Api/PetTemplate.Api.csproj" />
    <Project Path="src/PetTemplate.Db/PetTemplate.Db.csproj" />
    <Project Path="src/PetTemplate.Help/PetTemplate.Help.csproj" />
    <Project Path="src/PetTemplate.Services/PetTemplate.Services.csproj" />
    <Project Path="src/PetTemplate.Shared/PetTemplate.Shared.csproj" />
    <Project Path="src/PetTemplate.Web/PetTemplate.Web.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/PetTemplate.Tests/PetTemplate.Tests.csproj" />
  </Folder>
  <Folder Name="/tools/">
    <Project Path="tools/HelpCheck/HelpCheck.csproj" />
  </Folder>
</Solution>
```

Папки решения совпадают с каталогами на диске: `src/` и `tests/` лежат физически;
клиент — в `src/PetTemplate.Web/web` (раньше лежал в корневом `PetTemplate.Web/`). Проект
тестов один — `PetTemplate.Tests`. Пути знают workflow, `Dockerfile`, хуки,
скрипты i18n клиента (`src/PetTemplate.Web/web/tools/*.mjs`, корень репозитория — три уровня
вверх от `web/`), `template.json` и ссылки в документах.

Общие свойства сборки — `Directory.Build.props` (`net10.0`, `Nullable`, `ImplicitUsings`,
анализатор `SonarAnalyzer.CSharp` во всех проектах), версии пакетов — централизованно
в `Directory.Packages.props` (`ManagePackageVersionsCentrally`).

Новые проекты (`PetTemplate.Mail`, `PetTemplate.Help` и т. п.) получают имя с префиксом
`PetTemplate.` — `dotnet new` заменяет его на имя нового проекта сам (`sourceName`
в `.template.config/template.json`).

### 2.2 `nuget.config`

`nuget.config` в корне — один источник `nuget.org`: закрытых фидов и токенов для restore нет,
чистый клон и раннер собираются без учётных данных.

Очередь команд — пакеты `AsuTrack.LocalQueue`, `AsuTrack.LocalQueue.PostgreSql`
и `AsuTrack.LocalQueue.OpenTelemetry` с nuget.org (пространства имён в коде — `LocalQueue.*`).
Постановка — своя: `db.Enqueue(command)` кладёт строку в таблицу `local_queue` тем же
`SaveChanges`, что и доменное изменение (outbox); колонки таблицы — ровно те, что ждёт
хранилище пакета под PostgreSQL, тип команды — `FullName`, тело — JSON по умолчанию.
Разбирают воркеры пакета: `AddCommandQueueStorage(config)` один раз, затем
`AddCommandQueueWorker(...)` на каждый воркер (`src/PetTemplate.Api/Workers/CommandQueueRegistration.cs`,
`mail.md`, 4).

### 2.3 `README.md` — оглавление

Корневой `README.md` — оглавление, а не содержание: стек, ссылка на таблицу спецификаций
(`specs/README.md`), справка `help/`, таблица `agents.md` подсистем, `tech-debt/README.md`,
запуск, проверки до коммита и до пуша, ворота выкатки, конфигурация
и секреты, как подключить поиск к своей сущности, установка шаблона и создание проекта
(`dotnet new pet`). Дублировать документы в нём не нужно — копия расходится с оригиналом первой.

Отдельного списка «что настроить в проекте, созданном из шаблона» (домен, хуки, первая
миграция, секреты) в `README.md` нет — раздел 13.

### 2.4 Чего в репозитории нет

- **Артефактов сборки.** Корневой `.gitignore` — `bin/`, `obj/`, `out/`, `.artifacts-tests/`,
  `tests.log`, `tests.exit`; сборка клиента `dist/` — в `src/PetTemplate.Web/web/.gitignore`.
- **Личных файлов** — `/plan/`, `.idea/`, `.claude/settings.local.json`, `.claude/worktrees/`,
  `*.DotSettings.user`, `.DS_Store`. В проект из шаблона они тоже не копируются — `exclude`
  в `.template.config/template.json`.
- **Файлов сервера** — `IaC/nginx/conf/admin-allow.inc`, `admin-allow-open.inc`, `.env`.
- **Мёртвых файлов.** Закомментированный код, `Migrations/Helper.txt` и `.vscode`
  на несуществующие проекты удалены.

---

## 3. Локальный запуск

Корневой `docker-compose.yml` — инфраструктура для `dotnet run`: PostgreSQL той же версии, что
в `IaC/` (`postgres:18` на стандартном `5432`, база `pettemplatedb`, пользователь `postgres`,
пароль `1234` — только для разработки; шаблон — одна база на машину, а занятый порт меняется
в одном месте вместе со строкой в `appsettings.Development.json`), Qdrant
(`qdrant/qdrant:v1.20.0`, `6333`/`6334` на `127.0.0.1`) и эмбеддер (llama.cpp, `8080` на `127.0.0.1`;
`specs/search.md`, 3). Без `version:` (compose v2 его не читает), с именованными томами у базы,
Qdrant и весов эмбеддера.
API — `cd src/PetTemplate.Api && dotnet run`, клиент — `cd src/PetTemplate.Web/web && npm start`
(дев-сервер ходит в API через `proxy.conf.json`), админка — `npm run start:admin` на `4201`
(`specs/admin.md`, раздел 4).

Подключение к базе в трёх случаях (локально, с прода изнутри докера, с прода через SSH-туннель)
описано в `src/PetTemplate.Db/README.md`: главный
источник путаницы — хост и порт, `localhost:<порт дев-базы>` верен ровно для одного из трёх.

### 3.1 PostgreSQL

База шаблона — PostgreSQL. Что от этого зависит:

- **Провайдер** — `Npgsql.EntityFrameworkCore.PostgreSQL`, `UseNpgsql(...)`
  (`ServicesExtensions.AddDbContext`).
- **Имена в базе — snake_case**: `EFCore.NamingConventions`,
  `.UseSnakeCaseNamingConvention()` рядом с `UseNpgsql`. C#-модель остаётся PascalCase.
  Имена ограничений тоже snake_case (`pk_users`, `ix_users_user_name`). Ручной SQL
  в `IaC/DEPLOYMENT.md`, дашбордах и workflow — без кавычек, snake_case.
- **Уникальность.** Нарушение — `PostgresException.SqlState == "23505"`;
  какое ограничение — по `ConstraintName`. `DbErrors` сверяет его с именами ограничений шаблона.
  Правило — в `src/PetTemplate.Api/agents.md`.
- **Регистр — `citext`.** Postgres сравнивает строки с учётом регистра. Колонки, которые сравниваются без учёта регистра (логин `user_name`,
  почта `email`), — типа `citext`: `HasPostgresExtension("citext")` в `ApplicationContext`,
  `.HasColumnType("citext")` у свойства. Так `==`, `Contains`, `StartsWith` и уникальный индекс
  не учитывают регистр без особого кода: `Masha` и `masha` — один логин,
  `ix_profiles_user_name` их не пустит. Отдельная колонка `NormalizedUserName` с `citext` не нужна — сравнения идут по `UserName`,
  а нормализация `UserNameRule` остаётся только правилом ввода. Длину у `citext` база
  не держит — её держит валидация DTO.
- **Время.** `timestamptz` у Npgsql требует `DateTime.Kind = Utc` (запись `Local`
  падает `ArgumentException`), читается обратно тоже `Utc` — все `DateTime` в сущностях
  и сервисах в UTC (`DateTime.UtcNow`). Длинный текст — `text`, двоичное — `bytea`, JSON — `jsonb`.
- **Конкуренция — `xmin`.** Своей колонки версии нет: системная колонка Postgres `xmin` меняется при каждом обновлении строки сама. В сущности —
  `public uint Version { get; set; }`, в конфигурации — `builder.Property(x => x.Version).IsRowVersion();`
  (Npgsql сопоставит с `xmin`, в таблицу колонка не добавляется; пример — `AiModel`). Конфликт —
  `DbUpdateConcurrencyException`. Токен по своему полю (`IsConcurrencyToken()` на `Status`,
  как у `AdminInvitation`) от СУБД не зависит. Правило —
  в `src/PetTemplate.Api/agents.md`.
- **Миграции.** Начальная `Init` собрана под PostgreSQL (Npgsql) и создаёт всю схему;
  на проде её накатывает бандл миграций на шаге выкатки (раздел 6), тесты раскатывают ею
  шаблонную базу. Контекст для `dotnet ef` и бандла — `ApplicationContextFactory` (без хоста
  API: бандлу не нужны его секреты), настройки контекста у API и бандла общие (`Configure`).
  Что проверять в ней — `src/PetTemplate.Db/README.md`, «Первая миграция `Init`».
- **Тесты** — Testcontainers `PostgreSql`, один сервер на прогон, каталог данных в tmpfs,
  `fsync=off`, `synchronous_commit=off`, `full_page_writes=off`; база каждого теста — копия
  базы-шаблона со схемой (`CREATE DATABASE … TEMPLATE`, `tests/PetTemplate.Tests/Containers/PostgresServer.cs`;
  `tech-debt/test-schema-per-test.md`). Qdrant — свой контейнер (`QdrantServer.cs`).
- **Полнотекст** — вычисляемая колонка `tsvector` с GIN-индексом,
  словари `russian` (`FullTextSearch` в `src/PetTemplate.Db/Extensions`, `specs/search.md`, 7).

---

## 4. Проверки до коммита и до пуша

`git config core.hooksPath .githooks` — один раз на клон; сам `dotnet new` хуки не включает
(раздел 13). Гоняется только то, чего коснулись.

| Когда | Что | Сколько |
|---|---|---|
| `pre-commit`, тронут `src/PetTemplate.Web/web/{src,i18n,tools}` | `i18n:check`, `i18n:lint` | секунды |
| `pre-commit`, тронуты `*.cs`, словари, `*.routes.ts` | `i18n:codes` — код отказа объявлен на сервере, переведён в `en`, словарь грузится маршрутом | секунды |
| `pre-commit`, тронуты `*.cs`, `*.csproj`, `*.props`, `*.slnx` | `dotnet build PetTemplate.slnx -v q` | секунды |
| `pre-push`, тронут `src/PetTemplate.Web/web/` | `npx ng test pet-template --watch=false --browsers=ChromeHeadless` | секунды (≈6 с со сборкой) |

`pre-push` без `node_modules` не пропускает молча, а останавливает пуш с подсказкой (`npm ci`
или `--no-verify`); ветку, которой ещё нет на сервере, сравнивает с `origin/main`, а без него —
гоняет тесты целиком.

`dotnet test` в хуки не вешается: тесты на настоящей базе через Testcontainers — это минуты
и докер под ними, а те же тесты уже стоят воротами в `publish-api.yml`. Локально — руками.
Спеки админки (`npm run test:admin`) на `pre-push` не вешаются: отдельный сайт, правится редко
(`specs/admin.md`, раздел 4). Обойти разово — `--no-verify`.

Проверяется рабочее дерево, а не индекс: словари, ключи и сборка живут файлами, а не строками.

---

## 5. Ворота выкатки

| Workflow | Триггер | Раннер | Что проверяет до выкатки |
|---|---|---|---|
| `publish-api.yml` | `workflow_dispatch` | self-hosted | `HelpCheck lint`; `dotnet test --solution PetTemplate.slnx` — PostgreSQL и Qdrant через Testcontainers, в фоне параллельно сборке; упали — образ не собирается |
| `publish-angular.yml` | `workflow_dispatch` | тесты — `ubuntu-latest`, сборка и деплой — self-hosted | `web-tests.yml`; затем `npm run build`, которую роняет `maximumError` бюджета бандла |
| `publish-admin.yml` | `workflow_dispatch` | то же | `web-tests.yml`; `ng build admin` со своим бюджетом (`maximumError` 600 КБ) |
| `web-tests.yml` | `workflow_call` | `ubuntu-latest` | `i18n:check`, `i18n:lint`, `i18n:codes`, `ng test pet-template`, `ng test admin` — там есть Chrome, ставить его на машину с продом незачем |
| `help-check.yml` | `pull_request` | `ubuntu-latest` | `HelpCheck lint` и `HelpCheck map` — справка без техники и сверена со спецификациями (`specs/help.md`, 5.2) |

На ПР — только проверка справки: ей ничего не нужно от хоста и секретов, а остальное гоняют
хуки и ворота выкатки.

Служебные workflow без ворот: `rollback-api.yml`, `list-api-versions.yml` (после списка зовёт
`cleanup-old-images.yml`), `cleanup-old-images.yml`, `publish-help.yml` (зовёт `publish-api.yml`,
вручную — правка справки без выкатки, `specs/help.md`, 6.4), `backup-db.yml` (раздел 11),
`admin-access.yml` — открыть и закрыть админку и Grafana на время, плюс запуск по расписанию
раз в 15 минут закрывает просроченное окно (`specs/admin.md`, раздел 7; Grafana — раздел 10).

---

## 6. Выкатка API

`publish-api.yml`:

0. `HelpCheck lint` — справка с техническими словами останавливает выкатку сразу
   (`specs/help.md`, 6.4).
1. `dotnet restore`/`build`/`publish` `src/PetTemplate.Api/PetTemplate.Api.csproj` в `Release`;
   параллельно в фоне — `dotnet test --solution PetTemplate.slnx` с отдельным `ArtifactsPath` (иначе два
   `dotnet` пишут в одни `obj/`); бандл миграций `efbundle` (`dotnet ef migrations bundle`).
2. Ожидание тестов — ворота: красные или не уложились в 20 минут — `exit 1` до `docker build`.
3. `docker build --tag pettemplate-api:<yyyy_MM_dd__HH_mm_ss>` из `src/PetTemplate.Api/Dockerfile`
   (образ `aspnet:10.0`, копирует готовую публикацию; сборки внутри образа нет).
4. Миграции: `efbundle` в контейнере `aspnet:10.0` в сети `nginx-proxy-man` под владельцем базы
   (`MIGRATIONS_DB_CONNECTION_STRING`; секрет пуст или миграция упала — `exit 1`, старый
   контейнер не тронут).
5. `docker rm -f dapi`, `docker run --restart unless-stopped -p 127.0.0.1:5000:8080 --name dapi
   --network nginx-proxy-man` с переменными окружения из секретов и переменных (раздел 9),
   `OTEL_*` (раздел 10) и `ASPNETCORE_ENVIRONMENT=Release`; тег `pettemplate-api:latest`.
   Справка хоста `~/pettemplate-help` монтируется только на чтение в `/app/help`.
6. `publish-help.yml` — справка того же коммита в `~/pettemplate-help/releases/<sha>`, ссылка
   `current` на неё (`specs/help.md`, 6.4); `cleanup-old-images.yml` — оставить пять
   последних версий плюс `latest`, бегущую не трогать.

**Порт только на `127.0.0.1`:** снаружи API достижим лишь через nginx. Открытый порт обходит
замок админки (`specs/admin.md`, раздел 7), а `X-Forwarded-For`, по которому лимитер входа считает
адреса, подделывается. API берёт из `X-Forwarded-For/Proto` только последнее звено
(`ForwardLimit = 1`) — его дописывает сам nginx (`$proxy_add_x_forwarded_for`); списки известных
прокси очищены, потому что адрес nginx в сети докера не постоянный
(`ServicesExtensions.ConfigureForwardedHeaders`). Держится это на том, что до порта API снаружи
не дойти.

**Миграции накатывает шаг выкатки, а не приложение.** `publish-api.yml` собирает бандл
(`dotnet ef migrations bundle`, `dotnet-ef` — из манифеста `dotnet-tools.json`) и после тестов,
до замены контейнера, запускает его в контейнере `aspnet:10.0` в сети базы под владельцем
(`MIGRATIONS_DB_CONNECTION_STRING`, раздел 8). Упали миграции — старый контейнер не тронут.
API на старте схему не трогает и не может: его роль — только DML. Старый API работает, пока
идут миграции, а откат образа (`rollback-api.yml`) схему не откатывает — поэтому миграции пишутся expand → датафикс → contract:
каждая выкатка не ломает прод, а откат образа не требует отката схемы. На пустой боевой базе
всю схему создаёт начальная миграция `Init` (3.1).

**Qdrant обязателен, как база** (через него идёт поиск по смыслу, `specs/search.md`):
`QdrantInitializer` на старте создаёт коллекции и без Qdrant роняет API — так и задумано.
Эмбеддер (`embeddingscontainer`) — нет: без него индексация ждёт в очереди, а поиск идёт
по словам (`specs/search.md`).
Контейнер `qdrantcontainer` стоит в `IaC/docker-compose.yml` (раздел 8), а адрес —
в `appsettings.Release.json` (`QdrantOptions:Host = qdrantcontainer`, порт `6334`; там же
`SearchOptions:EmbeddingsEndpoint`): адреса соседних контейнеров — не секреты, в переменные
окружения им не надо.

**Откат** — `rollback-api.yml`: `previous`, `latest` или тег по дате; тот же `docker run`
с теми же переменными и проверка, что контейнер поднялся. **Версии** — `list-api-versions.yml`.

**`DOTNET_VERSION` в workflow — `10.0.x`**, как `TargetFramework` (`net10.0`).

---

## 7. Выкатка клиента и админки

`publish-angular.yml`: после `web-tests.yml` — `npm ci` (ровно `package-lock.json`, не
`npm install`), `npm run build -- --configuration production`, бэкап `~/frontend` в
`~/frontend-backup`, очистка, копирование `dist/pet-template/browser/.`. Откат — одной строкой из лога шага.

`publish-admin.yml`: то же для `ng build admin` (`dist/admin/browser`) → `~/adminka`,
бэкап `~/adminka-backup`.

Версии клиента у шаблона нет: ни `client-version.ts`, ни заголовка `X-Client-Version`, ни проверки
минимальной версии на сервере — *возврат:* когда у проекта появится второй клиент (мобильное
приложение).

---

## 8. Сервер (`IaC/`)

```
IaC/
├── docker-compose.yml   nginx, certbot, postgres, qdrant, эмбеддер, телеметрия (раздел 10)
├── .env.example         пароли и порты; .env — только на сервере
├── nginx/conf/
│   ├── nginx.conf
│   └── admin-allow.inc.example
├── postgres/init/       01-app-user.sh, 02-exporter-user.sh — пользователи приложения и экспортёра на пустом томе
├── otel-collector/      config.yaml
├── prometheus/          prometheus.yml
├── tempo/               tempo.yaml
├── grafana/
│   ├── provisioning/    источники данных, папка дашбордов, правила тревог
│   ├── on-hold/         telegram.yml — доставка тревог, пока проект её не включил (раздел 13)
│   └── dashboards/      *.json
├── setup-vm.sh          проверки докера, каталоги, сеть, симлинки, .env
├── README.md            что здесь лежит и как этим управлять
└── DEPLOYMENT.md        пошаговый деплой с нуля, секреты, админка, проверки
```

`IaC/DEPLOYMENT.md` — документация шаблона: что настроить в GitHub и на сервере проекту,
созданному из шаблона, а не план выкатки самого шаблона.

**`docker-compose.yml`:**

- `webserver` — `nginx:1.30` (стабильная ветка; патчи приходят сами), тома `./frontend`, `./adminka`, `./nginx/conf:ro`, certbot;
  сеть `nginx-proxy-man` (внешняя, создаётся `setup-vm.sh`).
- `certbot` — выпуск и продление; первый запуск — без https, потом сертификат, потом блок 443
  (порядок — в шапке файла и `IaC/DEPLOYMENT.md`, раздел 4).
- `database` — `postgres:18`, `container_name: dbcontainer`, **именованный том `db-data`**
  (`/var/lib/postgresql`: с 18-й версии образ держит данные в `18/docker` под ним; в анонимном томе данные уносит `docker compose down -v`
  или пересоздание), пароли из `.env` (`POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`,
  `APP_DB_USER`, `APP_DB_PASSWORD`, `APP_DB_OWNER`, `APP_DB_OWNER_PASSWORD`,
  `POSTGRES_EXPORTER_USER`, `POSTGRES_EXPORTER_PASSWORD`),
  healthcheck `pg_isready -h 127.0.0.1` (по TCP: во время первичной инициализации образ держит
  временный сервер только на сокете, и по сокету healthcheck сказал бы «готов» до того, как
  заведена роль приложения). Порт на хост не пробрасывается: API ходит по сети докера,
  человек — через `docker exec … psql`; для SSH-туннеля публикацию раскомментируют, и только
  на `127.0.0.1` (ufw докеру не указ).
- **Роли базы** — владелец `APP_DB_OWNER` (`pettemplate_owner`) и роль API `APP_DB_USER`
  (`pettemplate_app`). Заводит их `IaC/postgres/init/01-app-user.sh`, смонтированный
  в `/docker-entrypoint-initdb.d`: образ запускает его сам один раз, на пустом томе; скрипт
  идемпотентный, на живой базе его запускают руками (смена пароля, переезд, база шаблона v1 —
  `REASSIGN OWNED` передаёт объекты роли API владельцу). Владелец — владелец базы и всех
  объектов: под ним бандл миграций (раздел 6) делает DDL и `create extension citext`
  (доверенное расширение — владельцу базы хватает; схема `public` в Postgres 15+ принадлежит
  `pg_database_owner`). Роль API — только `SELECT/INSERT/UPDATE/DELETE` на таблицы и
  `USAGE, SELECT` на последовательности; таблицы будущих миграций достаются ей через
  `ALTER DEFAULT PRIVILEGES FOR ROLE` владельца. Ни у одной нет
  `SUPERUSER`/`CREATEDB`/`CREATEROLE`/`REPLICATION`/`BYPASSRLS`. Суперпользователь `postgres` — только
  для обслуживания и бэкапа: по сокету внутри контейнера вход доверенный (`local all all trust`
  у образа), снаружи контейнера — только по паролю (`scram-sha-256`).
- `qdrant` — `qdrant/qdrant:v1.20.0` (как `Qdrant.Client` 1.20 и Testcontainers),
  `container_name: qdrantcontainer`, том `qdrant-data` (не бэкапится — индекс восстанавливается
  реиндексом, `specs/search.md`, 3), порты только на `127.0.0.1` (`QDRANT_REST_PORT`,
  `QDRANT_GRPC_PORT`).
- `embeddings` — `ghcr.io/ggml-org/llama.cpp:server-b11096` (сборка закреплена: проверена на этой модели), `container_name: embeddingscontainer`,
  `multilingual-e5-small` в Q8 (модель меняется `EMBEDDINGS_REPO`/`EMBEDDINGS_FILE` в `.env`),
  том `embeddings-cache` на весь `/root/.cache` (нынешние сборки кладут веса в `huggingface/`,
  прежние — в `llama.cpp/`), портов на хост нет; веса качает сам при первом старте
  (`IaC/DEPLOYMENT.md`, раздел 4). Тег образа не закреплён: у llama.cpp теги —
  номера сборок (`tech-debt/embeddings-model.md`).
- телеметрия (раздел 10): `grafana`, `otel-collector`, `prometheus`, `tempo`, `postgres-exporter`,
  `node-exporter`, `cadvisor` — версии закреплены, портов на хост нет; тома `grafana-data`,
  `prometheus-data`, `tempo-data`; `webserver` и `grafana` — ещё и в сети `grafana-gateway`
  (`172.30.255.0/29`), оба с закреплёнными адресами `.2` и `.3`: со свободным Grafana, поднявшись
  раньше nginx, заняла бы его `.2` из `GF_AUTH_PROXY_WHITELIST`.
- **Пользователь экспортёра** — `POSTGRES_EXPORTER_USER` (`postgres_exporter`) со
  встроенной ролью `pg_monitor`: статистика `pg_stat_*` и `pg_settings`, без доступа к строкам
  таблиц, `CONNECTION LIMIT 3`. Заводит его `IaC/postgres/init/02-exporter-user.sh` так же, как
  пользователя приложения: на пустом томе сам, на живой базе — руками; пароль не задан —
  скрипт пропускает, база встаёт.
- Без `version:` — compose v2 его не читает и предупреждает.

**`nginx.conf`:**

- `server_name example.com www.example.com` в обоих блоках основного домена (домен — символ шаблона,
  разделы 12–13); `proxy_pass http://dapi:8080/api/` в обоих блоках;
- `location ~* ^/api/admin(/|$) { deny all; }` в блоках основного домена (`specs/admin.md`, раздел 7);
- `client_max_body_size` (5m) — по максимальному телу API: запись голоса ассистенту — до 4 МБ
  (`AssistantOptions:Transcription:MaxAudioBytes`, `specs/assistant.md`, 6.1) плюс обёртка multipart;
  поднимаете потолок записи — поднимайте и его;
- кэш: `index.html` — `no-cache`, бандлы с хешем — `immutable` на год, чтобы новый
  `index.html` не ждал истечения кэша;
- server-блоки `adminka.example.com` (80 → 301, 443 с `include admin-allow*.inc; deny all;`,
  `root /etc/nginx/html-admin`);
- server-блоки `grafana.example.com` (раздел 10): тот же замок, что у админки, `auth_request` на
  `Admin/Auth/Verify` и `proxy_pass` в сеть `grafana-gateway`.

**`setup-vm.sh`** — проверка прав на докер без `sudo`
и запрет запуска от root, каталоги `~/frontend`, `~/frontend-backup`, `~/adminka`,
`~/adminka-backup`, симлинки `IaC/frontend` и `IaC/adminka`, `certbot/`, сеть `nginx-proxy-man`,
наличие `docker-compose.yml`, `nginx.conf` и обоих скриптов `postgres/init/` (`01-app-user.sh`,
`02-exporter-user.sh`), `.env` из шаблона.
Каталоги `~/db-backups` и `~/pettemplate-help` заводят сами workflow.

---

## 9. Секреты GitHub Actions

| Секрет | Переменная в контейнере | Что |
|---|---|---|
| `API_DB_CONNECTION_STRING` | `ConnectionStrings__AppConnection` | `Host=dbcontainer;Port=5432;Database=pettemplatedb;Username=<пользователь приложения>;Password=…` |
| `MIGRATIONS_DB_CONNECTION_STRING` | — (шаг `apply migrations`, бандл миграций) | та же строка с владельцем базы `APP_DB_OWNER`; пусто — выкатка останавливается до замены контейнера |
| `JWT_SIGNING_KEY` | `JwtOptions__SigningKey` | `openssl rand -base64 48`; пустой или короче 32 байт — API не стартует |
| `ADMIN_JWT_SIGNING_KEY` | `AdminOptions__SigningKey` | не короче 32 байт и другой, чем `JWT_SIGNING_KEY`; иначе API не стартует (`specs/admin.md`, 3.3) |
| `TELEGRAM_BOT_TOKEN` | `Telegram__BotToken` | токен бота — подпись mini-app Telegram (`specs/accounts.md`, 5.6) |
| `VK_MINIAPP_PRIVATE_KEY` | `VkOptions__PrivateKey` | защищённый ключ mini-app VK (`specs/accounts.md`, 5.6) |
| `SMTP_PASSWORD` | `MailOptions__Password` | пароль SMTP-ящика (`specs/mail.md`, 5) |
| `ASSISTANT_API_KEY` | `AssistantOptions__Providers__main__ApiKey` | ключ поставщика модели ассистента (`specs/assistant.md`, 17) |

Пароль суперпользователя Postgres секретом не нужен: `backup-db.yml` зовёт `pg_dump` через
`docker exec` по сокету внутри контейнера, где вход доверенный, — пароль не нужен и в аргументы
не попадает.

Публичные id площадок (`TELEGRAM_BOT_ID` → `TelegramLoginOptions__BotId`, `VK_ID_APP_ID` →
`VkIdOptions__AppId`), адрес поставщика, модель ассистента и модель расшифровки голоса
(`ASSISTANT_ENDPOINT`, `ASSISTANT_MODEL`, `ASSISTANT_TRANSCRIPTION_MODEL`, `specs/assistant.md`,
6.2 и 17) — переменными (Variables), а не секретами. Своего ключа у голоса нет: расшифровка идёт
к тому же поставщику с тем же `ASSISTANT_API_KEY`. Набор `--env` один в `publish-api.yml`
и `rollback-api.yml` — новый секрет добавляется строкой в оба. Полный список с пояснениями —
`IaC/DEPLOYMENT.md`, раздел 6.

`JWT_SIGNING_KEY` — настоящий секрет: знающий его выпишет токен от имени любого пользователя.
Значение-заглушка в `appsettings.json` известно всем, у кого есть репозиторий (а у шаблона —
всем, кто его видел), поэтому на проде оно обязано быть заменено. API заглушку сам не отвергает:
незаданный секрет в выкатке оставит её в силе (раздел 13).

---

## 10. Телеметрия

Стек телеметрии — `IaC/DEPLOYMENT.md`, раздел 11 «Шлюз к инструментам» и дальше:

- API шлёт метрики и трассы по OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT=http://otelcollectorcontainer:4317`,
  `OTEL_SERVICE_NAME=pettemplate-api` в `publish-api.yml` и `rollback-api.yml`; без переменной
  не шлёт ничего — так локально и в тестах). Метрики — ASP.NET Core, HttpClient, рантайм .NET,
  письма (`MailMetrics`), очередь команд (метрики пакета `AsuTrack.LocalQueue.OpenTelemetry`,
  `AddLocalCommandQueueInstrumentation()`, meter `LocalQueue`: `local-command-queue-*`
  с меткой `commandType`, `dataSource` — `база.схема.таблица`, дашборд `queue`), ассистент и справка (`AssistantMetrics`), расход (`BillingMetrics`).
  Трассы — входящие запросы, походы наружу по HTTP и ход ассистента (источники
  `PetTemplate.Assistant` и `Experimental.Microsoft.Extensions.AI`, без текстов — `assistant.md`, 13.3).
- `otel-collector` раскладывает: метрики в `prometheus` (`--web.enable-otlp-receiver`, 30 дней
  или 2 ГБ), трассы в `tempo` (30 дней);
- `postgres-exporter`, `node-exporter`, `cadvisor` и `/metrics` самого
  Qdrant — Prometheus забирает сам (`IaC/prometheus/prometheus.yml`);
- `grafana` за шлюзом: своих учёток нет, вход — тот же, что в админку; nginx спрашивает
  `GET /api/Admin/Auth/Verify` по куке админской сессии (`specs/admin.md`, 3.3), кеширует «да»
  на минуту и ставит `X-WEBAUTH-USER`/`X-WEBAUTH-ROLE`; Grafana верит им только с адреса nginx
  в отдельной сети `grafana-gateway`. Замок `grafana.example.com` — тот же, что у админки. Портов
  на хост ни у кого из них нет.
- Источники данных, дашборды и тревоги — файлами в `IaC/grafana/`, не кликами.
  Дашборды `containers`, `host-node`, `queue`; `postgres` — grafana.com 9628 под метрики
  `postgres-exporter`, переменные Kubernetes убраны;
  `qdrant` — grafana.com 24603 как есть, источник подставлен (job `qdrant`, его `/metrics`
  на `6333`, и `qdrant` в тревоге `infra-target-down`); `assistant` («Ассистент») — ход, модели, справка,
  расход и счёт у поставщика (`assistant.md`, 13.4–13.5);
  `pettemplate-overview` свой: HTTP, почта, рантайм. Тревоги (`provisioning/alerting/rules.yml`) —
  инфраструктура, база, «Письма не уходят» и «Команда очереди не проходит» (`TryCount` строки
  больше пяти — с удвоением паузы это больше часа неудач, `mail.md`, 4); группа «Ассистент» —
  «Модель разомкнута», «Остаток у поставщика модели ниже порога», «Расчёт разошёлся с фактом
  у поставщика»; тревоги базы — по `pg_up` и `pg_stat_database_numbackends` /
  `pg_settings_max_connections`. Доставка тревог в Telegram — раздел 13.

Машина: стек рассчитан на два ядра и шесть гигабайт; проекту из шаблона сверить со своим тарифом.
Всё на одной машине с API — `tech-debt/telemetry-single-host.md`.

---

## 11. Бэкап базы

`backup-db.yml` — `docker exec dbcontainer pg_dump -U postgres -d pettemplatedb -Fc` тем
клиентом, что в контейнере (той же версии, что сервер), суперпользователем по сокету — пароль
не нужен (раздел 9). **Формат — custom, `-Fc`**: сжат сам, без конвейера `| gzip`, в котором код
возврата `pg_dump` теряется без `pipefail`; проверяется целиком — `pg_restore -f /dev/null`
прочитывает весь архив и падает на обрезанном;
восстанавливается выборочно и с `--clean`. SQL глазами — `pg_restore -f - файл`. Владельцы
объектов (`pettemplate_app`) в дампе сохраняются.
Проверка в workflow: `pg_dump` завершился с кодом 0, файл непустой, `pg_restore -f /dev/null`
прошёл; в лог — размер и число таблиц. Дамп пишется во временный `.part` и переименовывается
только после проверок. Хранятся последние `keep` (по умолчанию 14) дампов в `~/db-backups`
на VM (каталог `700`, файлы `600`), расширение `.dump`. Скачать — `scp`. Восстановление —
`drop database … with (force)`, `create database … owner pettemplate_app`,
`pg_restore --exit-on-error --single-transaction` при остановленном API; порядок —
`IaC/DEPLOYMENT.md`, раздел 10 «Бэкап и восстановление базы».
Перед выкаткой с миграцией, которая что-то удаляет, — обязательно. По расписанию бэкап
не запускается — только руками.

---

## 12. Решения шаблона

| Решение | Почему |
|---|---|
| PostgreSQL 18, `pg_dump`, `postgres-exporter`, `SqlState`, snake_case (3.1) | База шаблона; что от неё зависит — 3.1 |
| Своего прода нет; домен — `example.com`, символ шаблона: `dotnet new pet --domain <домен>` заменяет его везде (раздел 13) | Шаблон — заготовка; прод, живые данные и миграции expand/contract начинаются в проекте |
| Только ядро: аккаунты, workspace и права, почта, админка, телеметрия, поиск, справка, ассистент | Шаблон без бизнес-сущностей |
| Один тестовый проект `PetTemplate.Tests` | Проще |
| Ни мобильной выкатки, ни поддомена картинок, ни страниц справки на сайте; `publish-help.yml` есть — справка для ассистента (`specs/help.md`, 6.4) | Нет мобильных приложений и пользовательских картинок; показ справки на сайте — `tech-debt/help-client.md` |
| Ни `X-Client-Version`, ни проверки минимальной версии клиента (раздел 7) | Второго клиента нет |
| Имена `dapi`, `pettemplate-api`, `pettemplatedb` | `dotnet new` заменяет `pettemplate` на имя проекта сам |
| `net10.0`, `aspnet:10.0` | Текущий LTS .NET |

## 13. Решения и вопросы

Решено:

- **Физически `src/` и `tests/`**, клиент — `src/PetTemplate.Web` (2.1).
- **Выкатка — по кнопке**; автодеплоя с `main` нет (раздел 1).
- **База — PostgreSQL**, имена в базе snake_case, регистр логина и почты — `citext`,
  конкуренция — `xmin` (3.1).
- **Роли в Postgres** — владелец базы для миграций и роль API только с DML, обе без атрибутов
  суперпользователя; заводятся init-скриптом тома `IaC/postgres/init/01-app-user.sh` (8).
  Миграции — бандлом на шаге выкатки, не на старте API (6).
- **Формат дампа** — custom (`pg_dump -Fc`), проверка `pg_restore -f /dev/null` (11).
- **Доставка тревог в Telegram выключена по умолчанию** (10):
  `telegram.yml` лежит в `IaC/grafana/on-hold/`, вне провижининга. Без токена бота и id группы
  Grafana с ним в provisioning не стартует вовсе, а у свежего проекта из шаблона их нет — значит,
  включённая по умолчанию доставка роняла бы Grafana на первом же `docker compose up`. Тревоги
  срабатывают и видны в Alerting; включить — `IaC/DEPLOYMENT.md`, «Тревоги в Telegram»
  (переложить файл в `provisioning/alerting/`, `chatid`, `TELEGRAM_BOT_TOKEN` в `.env`).
- **Личные рабочие файлы не коммитятся:** `.gitignore` — `/plan/`, `out/`,
  `.idea/`, `.claude/settings.local.json`, `.claude/worktrees/`, `*.DotSettings.user`, `.DS_Store`.
  В проект из шаблона они не попадают и через `dotnet new` (`exclude` в `.template.config/template.json`).
- **Домен — символ шаблона `domain`:** `dotnet new pet -n Имя --domain мой-домен.ru` заменяет
  домен-заглушку шаблона во всех файлах — nginx и certbot, Grafana, workflow, `appsettings*.json`,
  клиент, письма, документация и тесты; поддомены `adminka.` и `grafana.` меняются вместе с ним,
  адреса почты (`info@…`) — тоже: это домен проекта.
- **`specs/`, `tech-debt/`, `help/` и `.claude/skills/` попадают в проект из шаблона:** это
  документация механизмов ядра и skill, который её правит; дальше их ведёт сам проект.
  `plan/` — нет.
- **Хуки после `dotnet new` не включаются сами** — строка `git config core.hooksPath .githooks`
  в `README.md`, «Новый проект из шаблона». `postActions` с `run script` спрашивают подтверждение
  и по-разному ведут себя на разных платформах, а `git init` в новой папке ещё не сделан.
- **Чек-лист «что настроить в новом проекте»** (домен, хуки, секреты и переменные
  GitHub) — `README.md`, «Новый проект из шаблона».

Не реализовано:

1. **Заглушки ключей подписи не отвергаются.** Длину обоих ключей и их несовпадение API
   проверяет на старте (9), но известное всем значение-заглушку из `appsettings.json` пропускает.

---

## 14. Где это в коде

| Что | Где |
|---|---|
| Решение и сборка | `PetTemplate.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config` |
| Шаблон `dotnet new` | `.template.config/template.json` |
| Игнор | `.gitignore`, `src/PetTemplate.Web/web/.gitignore` |
| Хуки | `.githooks/pre-commit`, `.githooks/pre-push` |
| Локальная инфраструктура | `docker-compose.yml`, `src/PetTemplate.Db/README.md` |
| Workflow выкатки | `.github/workflows/publish-api.yml`, `publish-angular.yml`, `publish-admin.yml`, `web-tests.yml`, `publish-help.yml` |
| Служебные workflow | `.github/workflows/rollback-api.yml`, `list-api-versions.yml`, `cleanup-old-images.yml`, `backup-db.yml`, `admin-access.yml`, `help-check.yml` |
| Образ API | `src/PetTemplate.Api/Dockerfile` |
| Старт API: миграции, OTel, forwarded headers | `src/PetTemplate.Api/Program.cs`, `src/PetTemplate.Api/Configs/ServicesExtensions.cs` |
| Конфигурация | `src/PetTemplate.Api/appsettings.json`, `appsettings.Development.json`, `appsettings.Release.json` |
| PostgreSQL в модели | `src/PetTemplate.Db/Entity/ApplicationContext.cs`, `src/PetTemplate.Db/Extensions/FullTextSearch.cs`, `src/PetTemplate.Services/Services/DbErrors.cs`, `src/PetTemplate.Db/Migrations/` (исключена из сборки) |
| Qdrant и очередь | `src/PetTemplate.Api/Workers/QdrantInitializer.cs`, `CommandQueueRegistration.cs`, `CommandQueueOptions.cs` |
| Метрики подсистем | `src/PetTemplate.Services/Mail/MailMetrics.cs`, `Assistant/AssistantMetrics.cs`, `Billing/BillingMetrics.cs` |
| Тестовая инфраструктура | `tests/PetTemplate.Tests/Containers/PostgresServer.cs`, `QdrantServer.cs` |
| Сервер | `IaC/docker-compose.yml`, `IaC/.env.example`, `IaC/setup-vm.sh`, `IaC/nginx/conf/`, `IaC/postgres/init/` |
| Телеметрия | `IaC/otel-collector/config.yaml`, `IaC/prometheus/prometheus.yml`, `IaC/tempo/tempo.yaml`, `IaC/grafana/` |
| Документация сервера | `IaC/README.md`, `IaC/DEPLOYMENT.md` |
