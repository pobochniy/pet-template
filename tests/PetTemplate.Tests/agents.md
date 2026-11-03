# Functional Tests

Функциональные (интеграционные) тесты для проверки работы API контроллеров с реальной базой данных.

## Правила написания тестов

- **Используем славянские имена** для пользователей: Oleg, Vasya, Petya, Igor, Sergey, Maxim, Dmitry, Andrey, Nikolay, Boris и т.д.
- **Используем смешные названия** для организаций: "Рога и Копыта", "Шарики и Палочки", "Пончики и Плюшки", "Бублики и Сушки", "Котики и Собачки", "Ёжики в Тумане", "Винтики и Шпунтики", "Крылышки и Ножки" и т.д.
- **Избегаем переменных типа** `user1`, `user2`, `organization1`, `organization2` - используем осмысленные имена
- **Используем UserBuilder напрямую**, если не требуется никакой специфики: `var oleg = new UserBuilder("Oleg").Please();`
- Для большинства тестов нужно пространство и владелец в нём со всеми правами —
  `Given.Owner`; клиент с заголовком `X-Workspace-Id` — параметр `workspaceId`:
```csharp
    var (workspace, token, oleg) = Given.Owner("Рога и Копыта");
    var (client, db) = await Given.ApiClient(x =>
    {
        x.Workspaces.Add(workspace);   // раньше прав: на него ссылается внешний ключ
        x.Profiles.Add(oleg);
    }, token, workspace.Id);
```
- Участник с отдельными правами — `new UserBuilder("Vasya").WithPermissions(workspace.Id, PermissionEnum.…)`:
  права ложатся строками в базу. В токене прав нет: сервер читает их из базы на каждом
  запросе (`SessionGuard`, `specs/authorization.md`, 2.3), так что права, поменянные тестом в базе
  мимо API, на запросе видны не сразу — кэш отметки живёт 30 секунд
- Токен из `UserBuilder.Please()` — без `iat`; пользователь из него обязан лежать в базе: каждый запрос
  сверяется с ней (`SessionGuard`, `specs/accounts.md`, 8.1), чужой id — `401`. Токен — как после
  входа паролем: с отметкой свежего входа `pwd` (8.6), привязка, отвязка и смена почты с ним проходят.
  Аккаунт без пароля, проверка отзыва сессий или давняя сессия без отметки входа —
  `TestSession.IssuedAt(userId, issuedAtUtc, dbArrange)`: токен с заданным временем выпуска;
  сессия, открытая входом в заданное время, — `TestSession.SignedInVia(userId, via, dbArrange)`. Пароль билдера — `UserBuilder.SuperAdminPassword`, хеш прежней схемы: вход
  заодно проверяет перехеш
- Отказы — `ShouldBeBadRequestWith(Messages.X)` (код 400) и `ShouldBeValidationWith("поле", код)` (422)
- «Свой и чужой»: два `Given.Owner` с разными пространствами в одной базе, заголовок — чужого
  (или query/тело с чужим id) — проверка изоляции по пространству

### Админка

Админка (`specs/admin.md`) ходит с токеном **другой схемы**: его выдаёт `POST /api/Admin/Login`,
а не `/api/Auth/LogIn`, и токен из `UserBuilder.Please()` в неё не пускает. Админа заводим
ролями в базе — `Given.SystemAdmin(...)` или `new UserBuilder("Oleg").WithAdminRoles(...)`,
входим через `Admin/Login` и кладём ответ в клиент: `client.UseAdminSession(result)`. Токен
с ролями мимо входа (в том числе без единой роли) — `Given.AdminClient(userId, roles, dbArrange)`;
роли в базе — `Given.AdminRoles(userId, …)`. Проверка токена урезает его роли до тех, что
в базе (спец. admin, 3.3): роль, которой нет в базе, токен не пустит — так проверяется «роль
сняли после входа». Отказ админки по содержанию — `400` с русским текстом без кода:
`ShouldBeBadRequest("текст")`. Тест, которому нужен сервис без HTTP (чистка, гонки) —
`Given.Services(dbArrange)` и сервисы из своей области.
Ключ подписи админки в тестах свой (`TestSettings.AdminSigningKey`, ставит `TestSettings.ConfigureAuth`).
Куки клиент теста не хранит (`HandleCookies = false` в `SetupApplication`): кука шлюза Grafana
(`AdminGatewayApiTests`) — `Secure` и на `.example.com`; тест, которому она нужна, шлёт
заголовок `Cookie` сам, как nginx в подзапросе `Admin/Auth/Verify`.
**Любой отказ админки — `404`**, а не `401`/`403`: так и проверяем. Лимит неудачных паролей
(`LoginLimiter`) — свой у каждого хоста: тест, который его исчерпывает, ему не мешает.

## Содержание

- [Общие принципы](#общие-принципы)
- [Структура тестов](#структура-тестов)
- [Подготовка данных](#подготовка-данных)
- [Именование тестов](#именование-тестов)
- [Примеры](#примеры)

---

## Общие принципы

### Что проверяем

- ✅ Конвейер "запрос-ответ" (HTTP API)
- ✅ Работу с базой данных
- ✅ Авторизацию и права доступа
- ✅ Валидацию входных данных
- ✅ Основные (Sunny Day) сценарии использования
- ✅ Условную логику и обработку ошибок

### Правила написания тестов

1. **Один тест = одна проверка** - тест должен вызывать API один раз и проверять конкретный сценарий
2. **Изолированность** - каждый тест работает с собственной базой данных
3. **Читаемость** - код теста должен быть понятен даже джуну
4. **Воспроизводимость** - тест всегда дает одинаковый результат
5. **Arrange-Act-Assert** - четкая структура теста

---

## Структура тестов

### Организация файлов

Тесты группируются по фичам (features) в папке `Features/`:

```
PetTemplate.Tests/
├── Features/
│   ├── Account/
│   │   ├── PasswordResetApiTests.cs
│   │   └── MailWorker.cs                 # Очередь писем с живым воркером
│   ├── Queue/
│   │   ├── QueueMetricsWorker.cs         # метрики очереди (MeterListener)
│   │   ├── QueueLanesWorker.cs           # свои воркеры очереди и аренда строки
│   │   └── QueueRegistrationTests.cs     # пауза повтора, выключенный разбор — без базы
│   ├── Assistant/                        # ассистент на FakeChatClient; TestPlan — тестовый раздел
│   ├── Search/                           # поиск на тестовой сущности (TestNotes), Qdrant в контейнере
│   ├── Help/                             # линтер, карта и ссылки на спецификации, индекс в памяти — без докера
│   ├── Health/                           # GET /health — база и Qdrant
│   ├── Auth/
│   │   ├── PasswordAuthApiTests.cs       # Тесты API endpoints
│   │   ├── TelegramLoginApiTests.cs      # вход Telegram на сайте (id_token)
│   │   ├── VkIdApiTests.cs               # вход VK ID (FakeVkId вместо сети)
│   │   ├── TelegramAuthApiTests.cs       # mini-app и его развилка
│   │   └── MiniAppLaunchUnitTests.cs     # Юнит-тесты бизнес-логики
│   ├── Invitation/
│   │   └── InvitationTests.cs
│   ├── Profile/
│   │   └── ProfileApiTests.cs
│   ├── Users/
│   │   └── UsersProfilesTests.cs           # состав пространства без контактов
│   ├── Workspace/
│   │   ├── WorkspaceTests.cs
│   │   ├── WorkspacePermissionsTests.cs    # свой/чужой workspace, 400/403/422
│   │   ├── WorkspaceLeaveTests.cs
│   │   └── WorkspaceCleanupTests.cs
│   └── Validation/
│       └── ValidationAttributeTests.cs
├── Arranges/Given.cs                     # Given.ApiClient и готовые миры
├── EntityBuilders/                       # UserBuilder, TelegramLoginBuilder, TelegramInitDataBuilder и др.
├── Asserts/                              # HttpAssert, QueueAssert
├── Mocks/                                # FakeMailSender, FakeVkId, FakeEmbedder, FakeChatClient
├── Containers/PostgresServer.cs          # общий PostgreSQL на прогон
├── Containers/QdrantServer.cs            # общий Qdrant на прогон, коллекция — своя у теста
├── TestHosts.cs                          # владелец приложений и баз теста
└── SetupApplication.cs                   # подъём приложения под тест
```

### Типы тестов

**1. `*ApiTests.cs`** - тесты HTTP API endpoints
- Вызывают API через HTTP клиент
- Проверяют запрос-ответ, валидацию, авторизацию
- Идут на настоящем PostgreSQL: у каждого теста своя база (см. ниже)

**2. `*Worker.cs`** - тесты воркеров и фоновых процессов (`Features/Account/MailWorker.cs`).
Очередь команд в API-тестах не разбирается: `ApiApplicationFactory` ставит
`CommandQueueOptions:Enabled = false` — воркеры пакета не регистрируются, обработчики остаются, и тест проверяет постановку — `QueueAssert.Mails(db)`. Обработку проверяет `Given.WorkerClient`
(воркер включён, опрос 100 мс) с заглушкой почты `Mocks/FakeMailSender` (`IsDown` — провайдер упал);
тест кладёт строку в `local_queue` сам и ждёт, пока она уйдёт из таблицы. Метрики
пакета (`Features/Queue/QueueMetricsWorker.cs`) слушает `MeterListener`: метр с тем же именем есть
у каждого хоста прогона, поэтому измерения отбираются по своему, уникальному для теста типу команды.
Суточная чистка (`DailyCleanupWorker`) с приложения под тестом снята (`ApiApplicationFactory`):
её первый проход удалял бы подготовленные тестом старые строки — тест чистки зовёт сервис сам
- Живут в **отдельном** файле, не вперемешку с быстрыми тестами фичи

**3. `*UnitTests.cs`** - юнит-тесты бизнес-логики
- Тестируют сервисы и бизнес-логику напрямую
- Не вызывают API и не поднимают базу

**Правило:** Для каждой фичи создаем папку с именем фичи и размещаем в ней тесты по типам.

### База теста — настоящий PostgreSQL

InMemory в тестах нет: он выполняет запросы в памяти и не замечает ни проекции,
уехавшей в клиентскую фильтрацию, ни внешнего ключа, ни `citext`, ни `xmin`
(спец. infrastructure). Поэтому все тесты с приложением идут на PostgreSQL —
и это дёшево:

- **Контейнер один на прогон** (`Containers/PostgresServer.cs`): `postgres:18`, данные в tmpfs,
  `fsync=off`, `synchronous_commit=off`, `full_page_writes=off` — надёжность после сбоя
  тестовой базе не нужна. Поднимается на первом обращении; останавливать его некому
  (`ICollectionFixture` отнял бы у классов параллельность) — его уносит Ryuk из Testcontainers
  после конца прогона. Нужен запущенный Docker.
- **Схема раскатывается один раз** — в шаблонную базу `pettemplate_template` тем же приложением,
  что и в тестах, миграциями (`MigrateAsync`), чтобы прогон ловил сломанную миграцию.
- **У теста своя база** — `CREATE DATABASE … TEMPLATE pettemplate_template`: копия файлов,
  а не DDL. Тест видит ровно то, что положил сам. Какая база у приложения — решает `dbName`
  в `SetupApplication`: приложения одного теста с одним `dbName` живут на одной базе
  (зарегистрировался одним, вошёл другим — `PasswordAuthApiTests.CanLogin`), с разными — на разных.
  `Given.ApiClient` даёт каждому вызову новую базу.

Замер на 2026-11-02: 970 тестов — 31 с локально (macOS, Docker Desktop) вместе со стартом контейнеров.

### Поиск — Qdrant в контейнере, тестовая сущность

Приложению под тестом Qdrant не нужен: `ApiApplicationFactory` снимает `QdrantInitializer`,
`SetupApplication` гасит адрес эмбеддера (`NullEmbedder`). Тесты поиска (`Features/Search`)
поднимают `SearchWorld`: Qdrant один на прогон (`Containers/QdrantServer`, как `PostgresServer`),
**коллекция своя у теста** (та же миграция, что в бою), эмбеддер — `Mocks/FakeEmbedder`
(один вектор на всё: проверяются фильтры, а не близость). Сущностей с текстом в ядре нет —
механизм проверяется на тестовой `TestNote` в своём контексте `TestNotesContext`: её таблица
встаёт в базу теста рядом со схемой приложения (`CreateTablesAsync`), в `ApplicationContext`
и шаблонную базу она не попадает. Новый тест изоляции — «свой и чужой» с одинаковым текстом.

### Ассистент — фейковая модель, тестовый раздел

Модели в тестах нет и в сеть ни один тест не ходит (`specs/assistant.md`, 15.4):
`Mocks/FakeChatClient` отдаёт записанные ответы по одному на обращение — текст (`Says`),
вызов инструмента (`Calling`), прозу с вызовами одним ответом (`Responds`), расход токенов
(`Costing`); отказ поставщика или «Остановить» посреди запроса — `OnAsk`, который бросает
или меняет базу. Мир — `Arranges/AssistantWorld`: пространство, автор, строка справочника
моделей и ход из той же сборки `AddAssistantRuntime`, что в бою, поверх базы теста (свои
у мира — только регистрации поверх); предохранители правятся в `world.Settings`
до `Runner`/`Handler`. Пишущих инструментов в ядре нет — план проверяется на тестовом разделе
`Features/Assistant/TestPlan`, подключённом как раздел проекта (`TestNoteSection`; заметки
и книги в памяти мира, `world.Notes`; чтение требует `WorkspaceInfoCrud`): создание,
рамка с `CanApplyImmediately = false`, правка с правом и сверкой, отказ раздела на названии
«сломай». Гонки (две вкладки, два экземпляра) — на втором соединении `world.NewContext()`.

### Приложение теста останавливается после теста

Каждый тест поднимает себе приложение, и его надо остановить: сам по себе хост живёт
до конца прогона вместе со всем хозяйством — DI-контейнером, пулом соединений,
`TestServer`, фоновыми службами и слежением за `appsettings.json`. Последнее упирается
в лимит inotify на сборочном сервере и в 512 слежений FSEvents на macOS (само слежение тесты
выключают, `TestHostDefaults.cs`).

Делает это `SetupApplication`: приложение он отдаёт владельцу сразу, как только получил.
Владелец — `TestScope` текущего теста из `TestHosts.cs`; заводит его на каждый тест
`[assembly: OwnHosts]` (xunit v3 `BeforeAfterTestAttribute`, тест находится по `TestContext.Current`).
После теста он останавливает приложения, **потом** дропает базы теста
(`DROP DATABASE … WITH (FORCE)`) — обратный порядок означал бы опросы мёртвой базы в логе.

**Тесту делать не надо ничего** — ни тому, что поднимается через `Given.ApiClient`,
ни тому, что создаёт фабрику руками (`new ApiApplicationFactory<Program>().SetupApplication(...)`).
Поднять приложение вне теста (в статическом поле, в конструкторе фикстуры) нельзя —
`SetupApplication` бросит исключение: у такого приложения нет владельца.

Что обвязка и правда останавливает хосты, стережёт `HostOwnershipTests`.

### Окружение и настройки приложения

- `ApiApplicationFactory` поднимает приложение в `Development` (настройки дев-машины); схему
  API не накатывает нигде — тестам её раскатывает `PostgresServer` миграциями.
- `SetupApplication` всегда перебивает `ConnectionStrings:AppConnection` (то, что осталось
  бы от `appsettings.json`, ведёт в дев-базу) и глушит лог хоста до `Error`, EF — совсем.
  Тест, которому нужен лог, перебьёт уровень своим `configureSettings`.
- Настройки авторизации тестов — `TestSettings.ConfigureAuth()`.

### Запуск

```bash
dotnet test --project tests/PetTemplate.Tests --filter-class "*ProfileApiTests"
dotnet test --solution PetTemplate.slnx    # перед сдачей — весь прогон
```

Раннер — Microsoft.Testing.Platform (xunit v3 4.x, включён в `global.json`): решение передаётся
через `--solution`, проект — через `--project`, фильтры — xunit (`--filter-class`, `--filter-method`,
`--filter-trait`), а не VSTest `--filter "FullyQualifiedName~…"`; `--logger` нет.

В CI тесты сервера — `api-tests.yml` на облачном раннере: в каждом ПР и воротами
`publish-api.yml` (спец. infrastructure, 5). В git-хуки `dotnet test` не вешается (докер
на каждый пуш) — перед пушем серверных правок гоняйте руками.

---

## Подготовка данных

Сначала ищем готовое в `Given`, потом — билдер, и только потом пишем своё. Повторилось
в двух тестах — в `Given`.

### `Given` — готовые миры (`Arranges/Given.cs`)

| Метод | Что даёт |
|---|---|
| `Given.ApiClient(dbArrange, token, workspaceId, dbName, settings, configureServices)` | приложение на своей базе теста; `token` — сессия, `workspaceId` — заголовок `X-Workspace-Id` |
| `Given.Owner(workspaceName, ownerName)` | пространство и владелец со всеми правами в нём: `(workspace, token, owner)` |
| `Given.Invitation(workspaceId, createdBy, permissions…)` | живое приглашение в пространство |
| `Given.SystemAdmin(roles…)`, `Given.AdminRoles(userId, roles…)` | админ с ролями в базе |
| `Given.AdminClient(userId, roles, dbArrange)` | клиент админки с токеном мимо входа |
| `Given.Services(dbArrange)` | сервисы без HTTP — чистка, гонки |
| `Given.WorkerClient(...)` | приложение с живым воркером очереди |

### Билдеры (`EntityBuilders/`)

- `UserBuilder("Vasya")` — пользователь; `.Please()` отдаёт `(token, profile)`. Права —
  `.WithPermissions(workspace.Id, PermissionEnum.…)` или `.WithAllPermissions(workspace.Id)`,
  основное пространство — `.WithPrimaryWorkspace(...)`, входы — `.WithTelegram(id)`, `.WithVk(id)`,
  `.WithoutPassword()`, контакты — `.WithContacts(email, phone)`, админка — `.WithAdminRoles(...)`.
- `WorkspaceBuilder("Котики и Собачки")` — пространство, `.CreatedAt(utc)` — давнее.
- `WorkspaceInvitationBuilder(workspaceId, createdBy)` — приглашение с правами и сроком.
- `TelegramLoginBuilder`, `TelegramInitDataBuilder` — подписанные данные входа Telegram.
- `AssistantFactBuilder` — память ассистента.

Сущности кладём в базу напрямую (`dbArrange`), а не через API: тест на список не должен
зависеть от создания. Через API — только когда проверяется само создание или его побочный эффект.

---

## Именование тестов

Имя — что проверяется, глаголом, без `Test_`, `_Success`, `_ShouldFail`:

```csharp
CreateReturnsSessionWithOwnerPermissions
FirstWorkspaceBecomesPrimary
OwnerOfOneWorkspaceIsForbiddenInAnother
ReturnsBadRequestWhenWorkspaceHeaderIsMissing
```

Зачем тест, если из имени не очевидно, — `<summary>` со ссылкой на раздел спецификации.

---

## Примеры

### Пример 1: создание через API

```csharp
/// <summary>
/// Первое пространство становится основным само (спец. authorization, 7.1)
/// </summary>
[Fact]
public async Task FirstWorkspaceBecomesPrimary()
{
    var (token, oleg) = new UserBuilder("Oleg").Please();
    var (client, db) = await Given.ApiClient(arrange => arrange.Profiles.Add(oleg), token);

    var response = await client.PostAsJsonAsync("/api/Workspace/Create", new WorkspaceDto { Name = "Винтики и Шпунтики" });

    await response.ShouldBeSuccessful();
    var created = await response.Content.ReadFromJsonAsync<WorkspaceCreatedDto>();
    Assert.Equal(created!.WorkspaceId, created.Authentication.Profile.PrimaryWorkspaceId);
    var profile = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == oleg.Id);
    Assert.Equal(created.WorkspaceId, profile.PrimaryWorkspaceId);
}
```

Ответ проверяем вместе с базой: `db` из `Given.ApiClient` смотрит в ту же базу, что приложение.

### Пример 2: свой и чужой

```csharp
/// <summary>
/// Все права владельца — но в своём пространстве. В чужом, названном заголовком,
/// у него нет ничего
/// </summary>
[Fact]
public async Task OwnerOfOneWorkspaceIsForbiddenInAnother()
{
    var (workspace, _, oleg) = Given.Owner("Рога и Копыта");
    var (ownWorkspace, token, vasya) = Given.Owner("Пончики и Плюшки", "Vasya");

    var (client, _) = await Given.ApiClient(db =>
    {
        db.Workspaces.AddRange(workspace, ownWorkspace);
        db.Profiles.AddRange(oleg, vasya);
    }, token, workspace.Id);

    var response = await client.GetAsync($"/api/Invitation/GetList?workspaceId={workspace.Id}");

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

Изоляция по пространству — два `Given.Owner` в одной базе и запрос с чужим id: в заголовке,
в query или в теле.

### Пример 3: два клиента на одной базе

```csharp
[Fact]
public async Task CanLogin()
{
    const string username = "Igor";
    var dbName = Guid.CreateVersion7().ToString();

    var (registerClient, _) = await new ApiApplicationFactory<Program>()
        .SetupApplication(dbName: dbName, configureSettings: TestSettings.ConfigureAuth());
    await registerClient.PostAsJsonAsync("/api/Auth/Register", new RegisterDto
    {
        UserName = username, Password = DefaultPassword, PasswordConfirm = DefaultPassword
    });

    var (loginClient, _) = await new ApiApplicationFactory<Program>()
        .SetupApplication(dbName: dbName, configureSettings: TestSettings.ConfigureAuth());

    var response = await loginClient.PostAsJsonAsync("/api/Auth/LogIn", new LoginDto
    {
        Login = username, Password = DefaultPassword
    });

    await response.ShouldBeSuccessful();
}
```

Одинаковый `dbName` — одна база на оба приложения (`Given.ApiClient` с `dbName` — то же).
Подготовка через API здесь оправдана: проверяется вход после настоящей регистрации.

---

## Что проверить у эндпоинта

- основной сценарий;
- без сессии и без права — `401`/`403`; админка — всегда `404`;
- валидация — `ShouldBeValidationWith("поле", код)`, отказ по смыслу — `ShouldBeBadRequestWith(Messages.X)`;
- граничные случаи — пусто, несуществующий id, дубликат;
- свой и чужой — данные другого пространства не видны и не меняются.

Один тест — один вызов API и одна проверка; значение, которое встречается в Arrange
и в Assert, — в переменную.
