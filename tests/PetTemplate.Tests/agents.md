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
  права ложатся и строками в базу, и клеймами `ws_permission` в токен
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
(`AdminLoginLimiter`) — свой у каждого хоста: тест, который его исчерпывает, ему не мешает.

## Содержание

- [Общие принципы](#общие-принципы)
- [Структура тестов](#структура-тестов)
- [Паттерны подготовки данных](#паттерны-подготовки-данных)
- [Именование тестов](#именование-тестов)
- [Примеры](#примеры)

Примеры ниже показывают паттерны на сущностях, которых в шаблоне нет (`Given.Admin`,
`Given.Organization`, эпики) — пространство и права см. выше (`Given.Owner`, `WorkspaceBuilder`,
`WorkspaceInvitationBuilder`, `Features/Workspace`).

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
│   ├── Help/                             # линтер и карта справки, индекс в памяти — без докера
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

Замер на 2026-10-31: 101 тест — 10 с вместе со стартом контейнера.

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
моделей и все сервисы хода поверх базы теста; предохранители правятся в `world.Settings`
до `Runner`/`Handler`. Пишущих инструментов в ядре нет — план проверяется на тестовом разделе
`Features/Assistant/TestPlan` (заметки и книги в памяти мира, `world.Notes`): создание,
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

В CI тесты сервера — ворота `publish-api.yml` (спец. infrastructure, 5). В git-хуки
`dotnet test` не вешается (докер на каждый пуш) — перед пушем серверных правок гоняйте руками.

---

## Паттерны подготовки данных

### 1. Arranges - готовые данные

Класс с захардкоженными данными для быстрого использования в тестах.

**Расположение:** `Arranges/`

**Пример:** `ArrangeEpics.cs`

```csharp
public static class ArrangeEpics
{
    // DTO для отправки в API
    public static readonly EpicDto TestEpic = new EpicDto
    {
        Id = 42,
        Priority = IssuePriorityEnum.high,
        Name = "Тестовый эпик",
        Description = "Описание",
        DueDate = new DateTime(2007, 1, 1)
    };
    
    // Entity для прямой вставки в БД
    public static readonly Atheneum.Entity.Epic TestEpicModel = new()
    {
        Id = 111,
        Priority = IssuePriorityEnum.high,
        Name = "Тестовый эпик",
        Description = "Описание",
        DueDate = new DateTime(2007, 1, 1)
    };
}
```

**Когда использовать:**
- ✅ Для простых тестов, где не нужна кастомизация
- ✅ Когда одни и те же данные используются в нескольких тестах
- ✅ Для тестов на `Create` (используем DTO)
- ✅ Для тестов на `Get/Update/Delete` (предварительно вставляем Entity в БД)

**Пример использования:**

```csharp
// Тест на Create - используем DTO
var dto = ArrangeEpics.TestEpic;
var response = await client.PostAsJsonAsync("/api/Epic/Create", dto);

// Тест на Get - предварительно вставляем Entity
var (client, db) = await Given.ApiClient(x => 
{
    x.Epics.Add(ArrangeEpics.TestEpicModel);
});
var response = await client.GetAsync($"/api/Epic/Get/{ArrangeEpics.TestEpicModel.Id}");
```

---

### 2. Given (Object Mother Pattern)

Класс с фабричными методами для создания часто используемых объектов с разумными значениями по умолчанию.

**Расположение:** `Arranges/Given.cs`

**Примеры:**

```csharp
public static class Given
{
    // Администратор со всеми ролями
    public static Profile Admin() => new UserBuilder("Admin", "admin@test.ru", "+79991234567")
        .WithAllRoles()
        .Please();
    
    // Пользователь Vlad с кастомными ролями
    public static Profile Vlad(params RoleEnum[] roles) => 
        new UserBuilder("Vlad", "vlad@test.ru", "+79091113344")
            .WithRoles(roles)
            .Please();
    
    // HTTP клиент с аутентификацией
    public static async Task<(HttpClient client, ApplicationContext db)> ApiClient(
        Action<ApplicationContext>? dbArrange = null, 
        Guid? userId = null)
    {
        // ...
    }
}
```

**Когда использовать:**
- ✅ **Всегда проверяем сначала Given** - возможно, нужный объект уже есть
- ✅ Для пользователей с типичными наборами ролей
- ✅ Для получения настроенного HTTP клиента
- ✅ Когда нужна небольшая кастомизация (например, роли пользователя)

**Пример использования:**

```csharp
// Используем готового админа
var admin = Given.Admin();

// Создаем Vlad с конкретными ролями
var vlad = Given.Vlad(PermissionEnum.issueRead, PermissionEnum.epicRead);

// Получаем клиент с аутентификацией
var (client, db) = await Given.ApiClient(x => x.Profiles.Add(admin));
```

---

### 3. Builders - гибкая кастомизация

Паттерн Builder для создания объектов с возможностью настройки любых полей.

**Расположение:** `EntityBuilders/`

**Пример:** `UserBuilder.cs`

```csharp
public class UserBuilder
{
    private readonly Profile _profile;
    private readonly User _user;
    private List<RoleEnum> _roles = new();

    public UserBuilder(string userName, string email, string phoneNumber)
    {
        _profile = new Profile
        {
            Id = Guid.CreateVersion7(),
            UserName = userName,
            Email = email,
            PhoneNumber = phoneNumber,
            Cash = 0
        };
        
        _user = new User
        {
            Id = _profile.Id,
            Login = userName,
            Password = BCrypt.Net.BCrypt.HashPassword("password123")
        };
    }

    public UserBuilder WithRoles(params RoleEnum[] roles)
    {
        _roles = roles.ToList();
        return this;
    }

    public UserBuilder WithAllRoles()
    {
        _roles = Enum.GetValues<RoleEnum>().ToList();
        return this;
    }

    public Profile Please()
    {
        _user.Roles = _roles;
        _profile.User = _user;
        return _profile;
    }
}
```

**Когда использовать:**
- ✅ Когда нужна гибкая кастомизация объекта
- ✅ Когда данных из `Given` недостаточно
- ✅ Для создания уникальных тестовых сценариев

**Пример использования:**

```csharp
var vlad = new UserBuilder("Vlad", "vlad@test.ru", "+79091113344")
    .WithRoles(PermissionEnum.issueRead)
    .Please();

var customUser = new UserBuilder("Test", "test@test.ru", "+71234567890")
    .WithAllRoles()
    .Please();
```

---

## Алгоритм выбора паттерна

```
Нужна сущность для теста?
    ↓
1. Есть ли она в Given?
    ├─ ДА → Используем Given
    └─ НЕТ → Переходим к шагу 2
    
2. Нужна ли кастомизация?
    ├─ НЕТ → Используем Arranges (захардкоженные данные)
    └─ ДА → Переходим к шагу 3
    
3. Стоит ли добавить в Given?
    ├─ ДА → Добавляем в Given и используем
    └─ НЕТ → Используем Builder напрямую
```

---

## Именование тестов

### Правила именования

1. **Начинаем с глагола:** `Can`, `Has`, `Should`, `Returns`, etc.
2. **Действие:** `Create`, `Get`, `Update`, `Delete`, `See`, etc.
3. **Сущность или специфика:** `Organization`, `OnlyOwnData`, `WithPermissions`, etc.

### Примеры

#### ❌ Плохо

```csharp
CreateOrganization_WithDifferentUsers_Success
GetOrganizationList_DifferentUsers_SeeDifferentOrganizations
UpdateOrganization_NotFounder_ShouldFail
Test_User_Can_Create_Organization
```

**Проблемы:**
- Слишком длинные
- Не начинаются с глагола
- Избыточные слова (`Success`, `ShouldFail`, `Test_`)

#### ✅ Хорошо

```csharp
CanCreateOrganization
CanSeeOnlyOwnOrganizations
CanGetOnlyUserOrganizations
CanUpdateOwnOrganization
CannotUpdateOtherUserOrganization
HasAccessToAssignedOrganizations
ReturnsNotFoundForMissingOrganization
```

**Преимущества:**
- Лаконично
- Понятно даже джуну
- Сразу видно, что проверяется

---

## Примеры

### Пример 1: Простой тест с Arranges

```csharp
[Fact]
public async Task CanCreateOrganization()
{
    // Arrange
    var admin = Given.Admin();
    var (client, _) = await Given.ApiClient(x => x.Profiles.Add(admin));
    var dto = ArrangeOrganizations.TestOrganization;
    
    // Act
    var response = await client.PostAsJsonAsync("/api/Organization/Create", dto);
    
    // Assert
    await response.ShouldBeSuccessful();
    var organizationId = await response.Content.ReadFromJsonAsync<Guid>();
    Assert.NotEqual(Guid.Empty, organizationId);
}
```csharp
[Fact]
public async Task CanGetOrganizationById()
{
    // Arrange
    var admin = Given.Admin();
    var organization = new Organization(Guid.CreateVersion7(), "Test Org", admin.Id)
    {
        Created = DateTime.UtcNow,
        IsActive = true
    };
    
    var (client, _) = await Given.ApiClient(x =>
    {
        x.Profiles.Add(admin);
        x.Organizations.Add(organization);
    });
    
    // Act
    var response = await client.GetAsync($"/api/Organization/Get/{organization.Id}");
    
    // Assert
    await response.ShouldBeSuccessful();
    var result = await response.Content.ReadFromJsonAsync<OrganizationDto>();
    Assert.Equal(organization.Id, result!.Id);
}
```

### Пример 3: Тест с несколькими пользователями

```csharp
[Fact]
public async Task CannotUpdateOtherUserOrganization()
{
    // Arrange
    var admin = Given.Admin();
    var vlad = new UserBuilder("Vlad", "vlad@test.ru", "+79091113344")
        .WithRoles(PermissionEnum.issueRead)
        .Please();
    
    var dbName = Guid.NewGuid().ToString();
    
    // Создаем организацию от имени admin
    var (adminClient, _) = await new ApiApplicationFactory<Program>()
        .SetupApplication(
            dbArrange: x =>
            {
                x.Profiles.Add(admin);
                x.Profiles.Add(vlad);
            },
            dbName: dbName,
            userId: admin.Id);
    
    var createDto = ArrangeOrganizations.TestOrganization;
    var createResponse = await adminClient.PostAsJsonAsync("/api/Organization/Create", createDto);
    var organizationId = await createResponse.Content.ReadFromJsonAsync<Guid>();
    
    // Переключаемся на Vlad (используем ту же БД)
    var (vladClient, _) = await new ApiApplicationFactory<Program>()
        .SetupApplication(
            dbArrange: null,
            dbName: dbName,
            userId: vlad.Id);
    
    var updateDto = new OrganizationDto
    {
        Id = organizationId,
        Name = "Hacked Name"
    };
    
    // Act
    var response = await vladClient.PutAsJsonAsync("/api/Organization/Update", updateDto);
    
    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

---

## Что следует проверить

- ✅ **Sunny Day** - основной сценарий с корректными данными
- ✅ **Авторизация** - доступ только для авторизованных пользователей
- ✅ **Права доступа** - проверка ролей и владения ресурсами
- ✅ **Валидация** - некорректные данные возвращают ошибку
- ✅ **Граничные случаи** - пустые списки, несуществующие ID, дубликаты
- ✅ **Изоляция данных** - пользователи видят только свои данные

---

## Принципы написания тестов

### 1. Один тест = одна проверка

Тест должен проверять **одну конкретную вещь** и вызывать API **один раз**.

#### ❌ Плохо - тест проверяет несколько вещей

```csharp
[Fact]
public async Task CanSeeOnlyOwnOrganizations()
{
    var admin = Given.Admin();
    var vlad = Given.Vlad();
    
    // Создаем организации через API (проверяем Create)
    await adminClient.PostAsJsonAsync("/api/Organization/Create", adminOrgDto);
    await vladClient.PostAsJsonAsync("/api/Organization/Create", vladOrgDto);
    
    // Получаем списки (проверяем GetList)
    var adminList = await adminClient.GetAsync("/api/Organization/GetList");
    var vladList = await vladClient.GetAsync("/api/Organization/GetList");
    
    // Проверяем оба списка
    Assert.Single(adminList);
    Assert.Single(vladList);
}
```

**Проблемы:**
- Проверяет и Create, и GetList
- Вызывает API 4 раза
- Если Create сломается, тест упадет, хотя GetList может работать
- Проверяет данные для двух пользователей

#### ✅ Хорошо - тест проверяет одну вещь

```csharp
[Fact]
public async Task CanSeeOnlyOwnOrganizations()
{
    // Arrange
    var admin = Given.Admin();
    var vlad = Given.Vlad();
    
    const string adminOrgName = "ООО Жирония";
    const string vladOrgName = "ОАО Рога и копыта";
    
    var adminOrg = Given.Organization(adminOrgName, admin.Id);
    var vladOrg = Given.Organization(vladOrgName, vlad.Id);
    
    var (client, _) = await Given.ApiClient(x =>
    {
        x.Profiles.Add(admin);
        x.Profiles.Add(vlad);
        x.Organizations.Add(adminOrg);
        x.Organizations.Add(vladOrg);
    }, admin.Id);
    
    // Act
    var response = await client.GetAsync("/api/Organization/GetList");
    var list = await response.Content.ReadFromJsonAsync<List<OrganizationListItemDto>>();
    
    // Assert
    Assert.NotNull(list);
    Assert.Single(list);
    Assert.Equal(adminOrgName, list[0].Name);
}
```

**Преимущества:**
- ✅ Проверяет только GetList
- ✅ Вызывает API один раз
- ✅ Данные подготовлены напрямую в БД (не зависит от Create)
- ✅ Проверяет данные только для одного пользователя
- ✅ Нет дублирования - название в переменной
- ✅ Использует `Given.Organization()` вместо дублирования кода

### 2. Используйте Given вместо дублирования

Если код повторяется - выносите в `Given`.

#### ❌ Плохо - дублирование кода

```csharp
var adminOrg = new Organization(Guid.CreateVersion7(), "Admin Org", admin.Id)
{
    Created = DateTime.UtcNow,
    IsActive = true
};

var vladOrg = new Organization(Guid.CreateVersion7(), "Vlad Org", vlad.Id)
{
    Created = DateTime.UtcNow,
    IsActive = true
};
```

#### ✅ Хорошо - используем Given

```csharp
var adminOrg = Given.Organization("ООО Жирония", admin.Id);
var vladOrg = Given.Organization("ОАО Рога и копыта", vlad.Id);
```

### 3. Используйте живые названия

Тестовые данные должны быть **реалистичными** и **разнообразными**.

#### ❌ Плохо - сухие тестовые названия

```csharp
"Test Organization"
"Admin Organization"
"Vlad Organization"
"Organization 1"
"Organization 2"
```

#### ✅ Хорошо - живые названия

```csharp
"ООО Жирония"
"ОАО Рога и копыта"
"ИП Петухов"
"ЗАО Ромашка"
"ООО Солнышко"
```

**Преимущества:**
- Тесты легче читать
- Сразу видно разные сущности
- Ближе к реальным данным

### 4. Избегайте дублирования в Assert

Выносите значения в переменные **только если используете их в двух местах** (например, в Arrange и Assert).

#### ❌ Плохо - дублирование строк

```csharp
var org = Given.Organization("ООО Жирония", admin.Id);
// ...
Assert.Equal("ООО Жирония", result.Name); // Дублирование!
```

#### ✅ Хорошо - используем переменную

```csharp
const string orgName = "ООО Жирония";
var org = Given.Organization(orgName, admin.Id);
// ...
Assert.Equal(orgName, result.Name); // Нет дублирования
```

#### ✅ Тоже хорошо - если используется только в одном месте

```csharp
var org = Given.Organization("ООО Жирония", admin.Id);
// Название не проверяется в Assert - переменная не нужна
```

**Правило:** Выносите в переменную только если значение используется **2+ раза**.

### 5. Подготовка данных: API vs БД

**Используйте прямую вставку в БД**, когда:
- ✅ Тест проверяет Get/Update/Delete
- ✅ Нужны данные для изоляции (другие пользователи, организации)
- ✅ Тест не должен зависеть от Create

**Используйте API**, когда:
- ✅ Тест проверяет Create
- ✅ Нужно проверить побочные эффекты создания

---

## Дополнительные материалы

- [Arranges/](./Arranges/) - готовые данные для тестов
- [EntityBuilders/](./EntityBuilders/) - билдеры для создания сущностей
- [Asserts/](./Asserts/) - вспомогательные методы для проверок
- [Containers/](./Containers/) - `PostgresServer`: общий PostgreSQL на прогон, базы тестов из шаблона
