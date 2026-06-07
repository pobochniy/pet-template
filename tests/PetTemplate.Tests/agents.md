# Functional Tests

Функциональные (интеграционные) тесты для проверки работы API контроллеров с реальной базой данных.

## Правила написания тестов

- **Используем славянские имена** для пользователей: Oleg, Vasya, Petya, Igor, Sergey, Maxim, Dmitry, Andrey, Nikolay, Boris и т.д.
- **Используем смешные названия** для организаций: "Рога и Копыта", "Шарики и Палочки", "Пончики и Плюшки", "Бублики и Сушки", "Котики и Собачки", "Ёжики в Тумане", "Винтики и Шпунтики", "Крылышки и Ножки" и т.д.
- **Избегаем переменных типа** `user1`, `user2`, `organization1`, `organization2` - используем осмысленные имена
- **Используем UserBuilder напрямую**, если не требуется никакой специфики: `var oleg = new UserBuilder("Oleg").Please();`
- Для большинства тестов нам нужен id организации и админ в ней с полными ролями:
```csharp
    var orgId = Guid.CreateVersion7();
    var oleg = Given.Admin(orgId);
    var org = Given.Organization("ООО Жирония", orgId, oleg);
```

## Содержание

- [Общие принципы](#общие-принципы)
- [Структура тестов](#структура-тестов)
- [Паттерны подготовки данных](#паттерны-подготовки-данных)
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
FunctionalTests/
├── Features/
│   ├── UserWallet/
│   │   ├── UserWalletApiTests.cs         # Тесты API endpoints
│   │   ├── UserWalletContainerTests.cs   # Тесты с реальной БД (TestContainers)
│   │   └── UserWalletUnitTests.cs        # Юнит-тесты бизнес-логики
│   ├── Epic/
│   │   ├── Epic.cs                       # API тесты
│   │   └── EpicWorker.cs                 # Тесты воркеров
│   └── OrganizationMember/
│       └── OrganizationMemberTests.cs
```

### Типы тестов

**1. `*ApiTests.cs`** - тесты HTTP API endpoints
- Вызывают API через HTTP клиент
- Проверяют запрос-ответ, валидацию, авторизацию
- Используют in-memory БД

**2. `*ContainerTests.cs`** - интеграционные тесты с реальной БД
- Используют TestContainers для запуска реальной БД
- Проверяют сложную логику, требующую реальной БД
- Тестируют воркеры, фоновые процессы

**3. `*UnitTests.cs`** - юнит-тесты бизнес-логики
- Тестируют сервисы и бизнес-логику напрямую
- Не вызывают API
- Используют in-memory БД

**Правило:** Для каждой фичи создаем папку с именем фичи и размещаем в ней тесты по типам.

---

## Паттерны подготовки данных

### 1. Arranges - готовые данные

Класс с захардкоженными данными для быстрого использования в тестах.

**Расположение:** `FunctionalTests/Arranges/`

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

**Расположение:** `FunctionalTests/Arranges/Given.cs`

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

**Расположение:** `FunctionalTests/ArrangeEntityBuilders/`

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

- [README-ApiClient.md](./README-ApiClient.md) - подробное описание работы с `Given.ApiClient`
- [Arranges/](./Arranges/) - готовые данные для тестов
- [ArrangeEntityBuilders/](./ArrangeEntityBuilders/) - билдеры для создания сущностей
- [Asserts/](./Asserts/) - вспомогательные методы для проверок
