# PetTemplate

Шаблон `dotnet new` для pet-проекта: ASP.NET Core API + Angular. Из него получается готовая
основа — аккаунты, права, инфраструктура и выкатка, — на которую проект кладёт свою предметную
область.

Этот файл — оглавление. Всё содержательное живёт в документах, на которые он ссылается:
поведение — в `specs/`, правила работы с кодом — в `agents.md` каждой подсистемы,
срезанные углы — в `tech-debt/`, сервер — в `IaC/`. Дублировать их здесь не нужно — копия
расходится с оригиналом первой.

## Стек

- **Бэкенд:** ASP.NET Core (.NET 10), EF Core, PostgreSQL 17 (Npgsql, snake_case)
- **Поиск:** Qdrant и эмбеддер llama.cpp (`multilingual-e5-small`), полнотекст — PostgreSQL `tsvector`
- **Клиент:** Angular
- **Тесты:** xUnit v3, `WebApplicationFactory`, PostgreSQL и Qdrant в Testcontainers
- **Инфраструктура:** Docker Compose, nginx, одна виртуалка ([`IaC/`](IaC))

## Спецификации — что за продукт и как он себя ведёт

[`specs/README.md`](specs/README.md) — таблица спецификаций: авторизация и права, аккаунты,
почта, админка, поиск, справка, инфраструктура.

## Справка — что продукт рассказывает о себе

[`help/`](help/) — короткие статьи для пользователя о том, как работает сайт; по ним будет
отвечать ИИ-ассистент (`specs/help.md`). Правится через skill `help-sync`
(`.claude/skills/help-sync`), не руками в обход карты `help/_map.yml`: `HelpCheck` в ПР
(`help-check.yml`) не пускает в статьи технические слова и сверяет карту со спецификациями.

## agents.md — как писать код в этом репозитории

Перед тем как трогать подсистему, прочитайте её `agents.md`: принятые паттерны, правила
тестов и типовые грабли.

| Подсистема | Документ |
|---|---|
| Клиент (Angular) | [`src/PetTemplate.Web/agents.md`](src/PetTemplate.Web/agents.md) |
| Бэкенд (ASP.NET Core) | [`src/PetTemplate.Api/agents.md`](src/PetTemplate.Api/agents.md) |
| Сущности и DTO | [`src/PetTemplate.Db/agents.md`](src/PetTemplate.Db/agents.md) |
| Тесты | [`tests/PetTemplate.Tests/agents.md`](tests/PetTemplate.Tests/agents.md) |

## Технический долг

[`tech-debt/README.md`](tech-debt/README.md) — таблица осознанно срезанных углов: один файл
на задачу, в каждом записано, почему так и при каком условии возвращаться.

## Запуск

Решение — [`PetTemplate.slnx`](PetTemplate.slnx): проекты в `src/`, тесты в `tests/`.
Миграции базы — [`src/PetTemplate.Db/README.md`](src/PetTemplate.Db/README.md).

```bash
docker compose up -d              # инфраструктура: база, Qdrant, эмбеддер (веса качает сам, 126 МБ)

cd src/PetTemplate.Api && dotnet run          # API на :5148

cd src/PetTemplate.Web/web
npm ci
npm start                         # dev-сервер, в API ходит через proxy.conf.json
npm run build                     # прод-сборка

npm run start:admin               # админка на :4201, тот же proxy.conf.json (specs/admin.md)
npm run test:admin                # её тесты — свой прогон, не на pre-push
npm run build:admin               # её сборка со своим бюджетом (600 КБ)
```

Тесты сервера — `dotnet test PetTemplate.slnx` (нужен запущенный Docker: тесты идут на настоящем
PostgreSQL и Qdrant в Testcontainers, [`tests/PetTemplate.Tests/agents.md`](tests/PetTemplate.Tests/agents.md)).
Тесты клиента — только headless: `npx ng test pet-template --watch=false --browsers=ChromeHeadless`
в `src/PetTemplate.Web/web` (проект назван явно: второй в `angular.json` — админка).

### Проверки до коммита и до пуша

```bash
git config core.hooksPath .githooks
```

Один раз на клон: хуки лежат в [`.githooks/`](.githooks), но git сам их не включает.

| Когда | Что | Сколько |
|---|---|---|
| `pre-commit`, тронут `src/PetTemplate.Web/web/{src,i18n,tools}` | `i18n:check` (словари сгенерированы и совпадают), `i18n:lint` (текст только через `t()`) | секунды |
| `pre-commit`, тронуты `*.cs`, словари, `*.routes.ts` | `i18n:codes` — у кода отказа сервера есть перевод, словарь грузится маршрутом | секунды |
| `pre-commit`, тронуты `*.cs`, `*.csproj`, `*.props`, `*.slnx` | `dotnet build PetTemplate.slnx -v q` | секунды |
| `pre-push`, тронут `src/PetTemplate.Web/web/` | `npx ng test pet-template --watch=false --browsers=ChromeHeadless` | секунды (≈6 с вместе со сборкой) |
| — (руками) | `npm run test:admin` — спеки админки; на `pre-push` не вешаются, ворота — `web-tests.yml` | секунды |

В хуках только то, что укладывается в секунды; тесты клиента — на пуше, а не на коммите:
коммитов за день много. `dotnet test` в хуки не вешается: тесты
на настоящей базе — это докер под ними на каждый пуш, а те же тесты стоят воротами
в `publish-api.yml`; тесты клиента — воротами выкатки клиента (`.github/workflows/web-tests.yml`).
Полная таблица — `specs/infrastructure.md`, разделы 4–5. Обойти хук на один раз — `--no-verify`.

### Пакеты из закрытого фида

Пакеты очереди (`localqueue`, `LocalQueue.*`) лежат в GitHub Packages (`pobhub`
в [`nuget.config`](nuget.config)). `packageSourceMapping` там же отправляет в `pobhub` только
их, всё остальное restore берёт с `nuget.org`: пока проект пакетов очереди не использует
(очередь писем своя до `LocalQueue.PostgreSql` — [`tech-debt/localqueue-postgres.md`](tech-debt/localqueue-postgres.md)),
токен не нужен. Как только они появятся, учётные данные `nuget.config` возьмёт из переменных
окружения — заведите их один раз в профиле оболочки (`~/.zshrc`):

```bash
export NUGET_GITHUB_USER=<логин GitHub>
export NUGET_GITHUB_TOKEN=<classic personal access token со scope read:packages>
```

Токен — GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic),
только `read:packages`. Источник `pobhub` в `nuget.config` перекрывает одноимённый из
глобального `~/.nuget/NuGet/NuGet.Config`: без переменных restore с пустым кэшем упадёт
с `401`, даже если глобально токен прописан. На раннере то же самое делает секрет
`NUGET_GITHUB_TOKEN`.

### Ворота выкатки

Красная ветка до прода не доезжает: проверки стоят в самих workflow выкатки. Выкатка —
руками, Actions → Run workflow; push в `main` ничего не выкатывает. Откат API —
`Rollback Api`, версии — `List Api Versions`, дамп базы — `Backup Db`
([`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), раздел 7). Админка (`adminka.example.com`) почти
всё время закрыта на nginx; открыть на 1–8 часов — `Admin Access` (`open`/`close`,
просроченное закрывает запуск по расписанию раз в 15 минут; `IaC/DEPLOYMENT.md`, раздел 8,
и [`tech-debt/admin-perimeter.md`](tech-debt/admin-perimeter.md)).

| Workflow | Что проверяет до выкатки |
|---|---|
| `publish-api.yml` | `dotnet test PetTemplate.slnx` — PostgreSQL через Testcontainers, в фоне параллельно сборке; упали — образ не собирается |
| `publish-angular.yml` | `web-tests.yml`; затем `npm run build`, которую роняет превышение `maximumError` бюджета бандла в `angular.json` |
| `publish-admin.yml` | `web-tests.yml`; `ng build admin`, которую роняет превышение своего бюджета (`maximumError` 600 КБ) |
| `web-tests.yml` | на облачном раннере (там есть Chrome): `i18n:check`, `i18n:lint`, `i18n:codes`, `ng test pet-template`, `ng test admin` |

### Конфигурация и секреты

`appsettings*.json` в репозитории — примеры и значения для разработки, а не секреты.
Боевые значения (строка подключения, ключи подписи JWT, токен бота, пароль SMTP) живут в секретах
GitHub и приходят в контейнер переменными окружения при выкатке (`publish-api.yml`
и `rollback-api.yml` — один набор) — список в `specs/infrastructure.md`, раздел 9,
и [`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), раздел 6. Адреса соседних контейнеров —
и настройки почты без пароля — не секреты: `src/PetTemplate.Api/appsettings.Release.json`
([`src/PetTemplate.Api/agents.md`](src/PetTemplate.Api/agents.md), «Конфигурация и секреты»).

## Поиск: подключить к своей сущности

В шаблоне поиск — только механизм: эмбеддер, Qdrant, индексация через очередь, слияние RRF,
реиндекс в админке. Сущностей с текстом нет — раздел проекта подключает свою
([`specs/search.md`](specs/search.md), раздел 9; образец — тестовая сущность
`tests/PetTemplate.Tests/Features/Search/TestNotes`):

1. **Тип** — значение в конец `SearchEntityType` (`src/PetTemplate.Shared/Enums`).
2. **Колонка для слов** — `NpgsqlTsVector SearchVector` у сущности и
   `HasFullTextSearch(x => x.SearchVector, x => new { x.Title, x.Body })` в модели, затем миграция.
3. **Источник текста** — `ISearchSource` своего типа (`Load`: `# Заголовок` + тело и `WorkspaceId`;
   `AllIds` — для реиндекса), регистрация — `services.AddSearchSource<T>()`.
4. **Индексация** — `db.Enqueue(SearchIndex.Of(тип, id))` тем же `SaveChanges`, что создание,
   правка и удаление; при удалении пространства — до удаления своих сущностей.
5. **Поиск** — `HybridSearch.Vector` (фильтр по пространству — обязательное поле `VectorQuery`)
   плюс свой запрос по `SearchVector` **с фильтром по `WorkspaceId`**, слияние — `HybridSearch.Ranked`.
6. **Эндпоинт и экран** — `GET {Раздел}/Search?q=` под правами списка, `shared-search-box` над списком.
7. **Тесты** — изоляция пространства, правка, удаление — по образцу `Features/Search/SearchIndexWorker`.
8. **Выкат** — полный реиндекс: админка → «Сводка» → «Поиск» → «Переиндексировать».

## Шаблон

### Установить из GitHub

```bash
git clone https://github.com/pobochniy/pet-template ~/proj/pet-template && dotnet new install ~/proj/pet-template
```

### Создать проект

```bash
dotnet new pet -n MyAwesomeProject -o folderName --domain myawesome.ru
```

Появится папка `folderName/`, в которой `PetTemplate` и `pettemplate` заменены на
`MyAwesomeProject` и `myawesomeproject`, а домен-заглушка шаблона — на значение `--domain`: в nginx и certbot, workflow, `appsettings*.json`, клиенте, письмах, документации
и тестах, вместе с `adminka.` и `grafana.` (`specs/infrastructure.md`, 13). Имя Angular-проекта
клиента `pet-template` (`angular.json`, `package.json`, `ng test pet-template`) остаётся
как есть: `dotnet new` заменяет только написания `PetTemplate` и `pettemplate`.

В проект попадают `specs/`, `tech-debt/`, `help/` и `.claude/skills/` — документация механизмов
ядра и skill справки; не попадают `plan/`, личные настройки Claude Code, `bin/`, `obj/`,
`node_modules/` и сборки клиента. Полный список — `exclude` в `.template.config/template.json`.

### Новый проект из шаблона: что настроить

1. **Git и хуки** — хуки шаблон сам не включает:
   ```bash
   cd folderName && git init && git config core.hooksPath .githooks
   ```
2. **Первая миграция `Init`**, если шаблон пришёл без неё (в `src/PetTemplate.Db/Migrations`
   лежит MySQL-версия, исключённая из сборки) — `src/PetTemplate.Db/README.md`,
   «Первая миграция `Init`». Без неё API на пустой боевой базе не стартует.
3. **Домен** — проект создан без `--domain`: домен сейчас `example.com`, заменить его поиском
   по всему репозиторию.
   DNS: A-записи домена, `www.`, `adminka.`, `grafana.` на виртуалку ([`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), 3, 8, 11).
4. **Пакеты из закрытого фида** — `NUGET_GITHUB_USER`/`NUGET_GITHUB_TOKEN` в профиле оболочки,
   когда проект начнёт брать пакеты очереди (ниже, «Пакеты из закрытого фида»).
5. **GitHub → Settings → Secrets and variables → Actions** ([`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), 6):
   - секреты: `API_DB_CONNECTION_STRING`, `JWT_SIGNING_KEY`, `ADMIN_JWT_SIGNING_KEY` (другой,
     чем `JWT_SIGNING_KEY`), `SMTP_PASSWORD`, `TELEGRAM_BOT_TOKEN`, `VK_MINIAPP_PRIVATE_KEY`,
     `ASSISTANT_API_KEY`, `NUGET_GITHUB_TOKEN` (`read:packages`; пуст — берётся `github.token`);
     пустые `TELEGRAM_BOT_TOKEN`, `VK_MINIAPP_PRIVATE_KEY`, `SMTP_PASSWORD`, `ASSISTANT_API_KEY`
     выключают свою функцию, API стартует;
   - переменные: `TELEGRAM_BOT_ID`, `VK_ID_APP_ID`, `ASSISTANT_ENDPOINT`, `ASSISTANT_MODEL`,
     `ASSISTANT_TRANSCRIPTION_MODEL` — пустая выключает свою кнопку или функцию.
6. **Сервер** — self-hosted runner, `IaC/.env` (`POSTGRES_PASSWORD`, `APP_DB_PASSWORD`,
   `POSTGRES_EXPORTER_PASSWORD`), сертификат, первый админ, адреса замка админки —
   [`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md) по порядку разделов.
7. **Почта** — `MailOptions` в `src/PetTemplate.Api/appsettings.Release.json` и DNS-записи
   SPF/DKIM/DMARC ([`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), 6, «Почта»; `specs/mail.md`).
8. **Telegram и VK** — бот, mini-app, приложение VK ID и адреса возврата
   ([`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), 6, «Вход через Telegram и VK»).

### Обновить

После пуша в репозиторий — склонировать заново и переустановить с `--force`:

```bash
rm -rf ~/proj/pet-template && git clone https://github.com/pobochniy/pet-template ~/proj/pet-template && dotnet new install ~/proj/pet-template --force
```

### Удалить

```bash
dotnet new uninstall ~/proj/pet-template
```

### Установить из рабочей копии

```bash
dotnet new install ./
```
