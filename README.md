# PetTemplate

Шаблон `dotnet new` для pet-проекта: ASP.NET Core API + Angular. Из него получается готовая
основа — аккаунты, права, инфраструктура и выкатка, — на которую проект кладёт свою предметную
область.

Этот файл — оглавление. Всё содержательное живёт в документах, на которые он ссылается:
поведение — в `specs/`, правила работы с кодом — в `agents.md` каждой подсистемы,
срезанные углы — в `tech-debt/`, сервер — в `IaC/`. Дублировать их здесь не нужно — копия
расходится с оригиналом первой.

## Стек

- **Бэкенд:** ASP.NET Core (.NET 10), EF Core, PostgreSQL 18 (Npgsql, snake_case)
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
npm run api:types                 # типы DTO клиента из OpenAPI документа API — после правки DTO на сервере
```

Тесты сервера — `dotnet test --solution PetTemplate.slnx` (нужен запущенный Docker: тесты идут на настоящем
PostgreSQL и Qdrant в Testcontainers, [`tests/PetTemplate.Tests/agents.md`](tests/PetTemplate.Tests/agents.md)).
Тесты клиента — Vitest в jsdom, без браузера: `npx ng test pet-template --watch=false`
в `src/PetTemplate.Web/web` (проект назван явно: второй в `angular.json` — админка).

### Проверки до коммита и до пуша

```bash
git config core.hooksPath .githooks
```

Один раз на клон: хуки лежат в [`.githooks/`](.githooks), но git сам их не включает. Что
и когда они гоняют — `specs/infrastructure.md`, раздел 4; обойти на один раз — `--no-verify`.
`dotnet test` в хуках нет: тесты сервера идут в ПР и воротами выкатки.

### ПР и выкатка

В каждом ПР на облачном раннере — тесты сервера (`api-tests.yml`), клиента (`web-tests.yml`)
и справки (`help-check.yml`: `HelpCheck lint`, `map`, `refs`). Выкатка — руками, Actions →
Run workflow; push в `main` ничего не выкатывает, а красная ветка до прода не доезжает: те же
тесты стоят воротами в workflow выкатки. Выкатка API ждёт `/health` (база и Qdrant) и сама
возвращает прежний образ, если новый не ответил. Таблица ворот —
`specs/infrastructure.md`, раздел 5; выкатка API — 6, клиента — 7.

Откат API — `Rollback Api`, версии — `List Api Versions`, дамп базы — `Backup Db` (ещё и каждую
ночь, копия вне VM — по настройке; [`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), разделы 7 и 10).
Админка (`adminka.example.com`) почти всё время закрыта на nginx; открыть на 1–8 часов —
`Admin Access` (`IaC/DEPLOYMENT.md`, раздел 8, и
[`tech-debt/admin-perimeter.md`](tech-debt/admin-perimeter.md)).

### Конфигурация и секреты

`appsettings*.json` в репозитории — примеры и значения для разработки, а не секреты.
Боевые значения живут в секретах и переменных GitHub и приходят в контейнер переменными
окружения при выкатке — таблицы в `specs/infrastructure.md`, раздел 9, и
[`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), раздел 6. Адреса соседних контейнеров —
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
   правка и удаление; точки снесённого пространства уносит ядро (`WorkspaceService.PurgeContents`).
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

1. **Git, хуки и биты исполнения** — `dotnet new` снимает бит исполнения со скриптов, а git
   хук без него молча пропускает (`specs/infrastructure.md`, 13):
   ```bash
   cd folderName && git init && git config core.hooksPath .githooks
   chmod +x .githooks/* IaC/*.sh IaC/postgres/init/*.sh   # бит уйдёт в git с первым коммитом
   ```
2. **Домен** — проект создан без `--domain`: домен сейчас `example.com`, заменить его поиском
   по всему репозиторию.
   DNS: A-записи домена, `www.`, `adminka.`, `grafana.` на виртуалку ([`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), 3, 8, 11).
3. **GitHub → Settings → Secrets and variables → Actions** — секреты и переменные по таблицам
   [`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), 6 (без `MIGRATIONS_DB_CONNECTION_STRING` выкатка
   не идёт, пустые ключи площадок и ассистента выключают свою функцию); копия бэкапа вне VM —
   `BACKUP_S3_*`, там же, 10.
4. **Сервер** — self-hosted runner, `IaC/.env` (`POSTGRES_PASSWORD`, `APP_DB_PASSWORD`,
   `APP_DB_OWNER_PASSWORD`, `POSTGRES_EXPORTER_PASSWORD`), сертификат, первый админ, адреса
   замка админки — [`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md) по порядку разделов.
5. **Почта** — `MailOptions` в `src/PetTemplate.Api/appsettings.Release.json` и DNS-записи
   SPF/DKIM/DMARC ([`IaC/DEPLOYMENT.md`](IaC/DEPLOYMENT.md), 6, «Почта»; `specs/mail.md`).
6. **Telegram и VK** — бот, mini-app, приложение VK ID и адреса возврата
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
