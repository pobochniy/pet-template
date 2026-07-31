# Permission Migrations: переход к workspace-контексту

## Суть изменений

Текущая система хранит permissions пользователя глобально (плоский список `PermissionEnum[]`).  
Цель — привязать все permissions к сущности `Workspace`: один пользователь может иметь разные права в разных workspace'ах.

Членство в workspace определяется наличием хотя бы одной записи в `UserInPermission` с данным `WorkspaceId` — отдельная сущность членства не нужна.

Паттерн полностью аналогичен проекту `asu`, где аналогичной сущностью является `Organization`.  
Бэкенд начинает читать хедер `X-Workspace-Id` для определения контекста; фронтенд добавляет этот хедер ко всем запросам через interceptor.

---

## Backend (`src/`)

### Новые файлы

| Файл | Что создать |
|------|-------------|
| `Lad.Shared/Constants/CustomClaimTypes.cs` | Константа `WorkspacePermission = "ws_permission"` — тип claim'а для прав, привязанных к workspace |
| `Lad.Db/Entity/Workspace.cs` | Сущность Workspace: `Id (Guid)`, `Name (string)`, навигационное свойство `UserPermissions` |
| `Lad.Api/Extensions/HttpContextExtensions.cs` | `GetWorkspaceId(this HttpContext)` — читает хедер `X-Workspace-Id`, парсит Guid, бросает `BadHttpRequestException` если хедер отсутствует или невалиден |
| `Lad.Services/Services/WorkspaceService.cs` | Методы: `Create(dto, userId)`, `GetList(userId)`, `GetById(workspaceId)`, `GetMembers(workspaceId)` — список участников получается запросом по `UserInPermission.WorkspaceId` с группировкой по `UserId` |
| `Lad.Api/Controllers/WorkspaceController.cs` | CRUD эндпоинты для workspace. Создание — без `[AuthorizePermissions]`; редактирование — `[AuthorizePermissions(PermissionEnum.workspaceInfoCrud)]`; `GetMembers` — `[AuthorizePermissions(PermissionEnum.workspaceMemberCrud, PermissionEnum.workspaceMemberRead)]` |

### Изменяемые файлы

#### `Lad.Db/Entity/UserInPermission.cs`
- Добавить свойство `WorkspaceId (Guid)` — FK на `Workspace`
- Добавить навигационное свойство `Workspace`
- Обновить `UserInPermissionConfiguration`: составной PK становится `(WorkspaceId, UserId, PermissionId)`, добавить `.HasOne(up => up.Workspace)`
- **Убрать** старую глобальную связь без workspace-контекста

#### `Lad.Db/Entity/ApplicationContext.cs`
- Добавить `DbSet<Workspace> Workspaces`
- Убрать `DbSet<WorkspaceMember>` — не нужен
- Зарегистрировать конфигурацию Workspace в `OnModelCreating`

#### `Lad.Shared/Enums/PermissionEnum.cs`
- Добавить новые значения для workspace-контекста:
  - `workspaceInfoCrud`
  - `workspaceMemberCrud`, `workspaceMemberRead` — управление правами участников (выдача/отзыв `UserInPermission`)
- Привести существующие значения в соответствие (убедиться, что числовые значения не конфликтуют)

#### `Lad.Shared/Dto/UserDto.cs`
- Заменить `IEnumerable<PermissionEnum> Permissions` на `Dictionary<Guid, List<PermissionEnum>> PermissionsByWorkspace`

#### `Lad.Services/Services/AuthService.cs`
- Методы `LogIn`, `GetOrRegisterUser`, `GetOrRegisterUserForVk`:
  - Вместо `db.UserInPermissions.Where(x => x.UserId == ...)` → группировать по `WorkspaceId`
  - Заполнять `UserDto.PermissionsByWorkspace` как `Dictionary<Guid, List<PermissionEnum>>`

#### `Lad.Api/Controllers/AuthController.cs`
- Метод `CreateToken(UserDto user)`:
  - Заменить `user.Permissions.Select(role => new Claim(ClaimTypes.Role, ...))` на итерацию по `user.PermissionsByWorkspace`
  - Каждый claim — тип `CustomClaimTypes.WorkspacePermission`, значение `"{workspaceId}|{(int)permission}"`

#### `Lad.Api/Middlewares/PermissionsValidation.cs`
- Читать хедер `X-Workspace-Id` (сейчас читается `ClaimTypes.Role` без контекста)
- Фильтровать claims типа `CustomClaimTypes.WorkspacePermission` начинающиеся с `"{workspaceId}|"`
- Добавить приватный метод `ValidateWorkspaceContext(ActionExecutingContext, Guid)`:
  - Проверять query-параметр `workspaceId` и свойство `WorkspaceId` в DTO
  - При несовпадении — `403 Forbidden`

#### `Lad.Api/Configs/ServicesExtensions.cs`
- В `AddAppServices()` зарегистрировать `WorkspaceService` через `AddTransient`

#### `Lad.Db/Migrations/`
- Создать новую EF-миграцию: `dotnet ef migrations add AddWorkspacePermissions -p src/Lad.Db -s src/Lad.Api`
- **Миграция данных (Data Migration)**:
  - Если база данных пустая, дополнительных действий по переносу данных не требуется.
  - Если в базе данных уже есть пользователи и их глобальные права, в миграции необходимо:
    1. Создать дефолтный системный Workspace (например, с фиксированным `Guid` или по одному дефолтному Workspace для каждого существующего пользователя).
    2. Перенести все записи из `UserInPermission` в новую схему, связав их с созданным WorkspaceId.

---

## Tests (`tests/`)

### Новые файлы

| Файл | Что создать |
|------|-------------|
| `Lad.Tests/EntityBuilders/WorkspaceBuilder.cs` | Builder для создания `Workspace` + набора `UserInPermission` для тестового пользователя |
| `Lad.Tests/Features/Workspace/WorkspaceTests.cs` | Тесты создания и получения workspace |
| `Lad.Tests/Features/Workspace/WorkspacePermissionsTests.cs` | Тесты проверки 401/403 при отсутствии хедера или недостаточных правах |

### Изменяемые файлы

#### `tests/Lad.Tests/EntityBuilders/UserBuilder.cs`
- Метод `WithPermissions(Guid workspaceId, ...)`:
  - При создании `UserInPermission` добавлять `WorkspaceId`
  - Сохранять пару `(workspaceId, permission)` для JWT-генерации
- Метод `GenerateJwtToken()`:
  - Claims типа `CustomClaimTypes.WorkspacePermission` со значением `"{workspaceId}|{(int)permission}"`
  - Убрать старые `ClaimTypes.Role` claims

#### `tests/Lad.Tests/SetupApplication.cs`
- Добавить необязательный параметр `workspaceId` (по аналогии с `organizationId` в asu)
- При наличии — использовать при формировании токена через `UserBuilder`

#### `tests/Lad.Tests/Arranges/Given.cs`
- Добавить вспомогательный метод для создания клиента с workspace-контекстом: выставлять хедер `X-Workspace-Id` на `HttpClient.DefaultRequestHeaders`

---

## Frontend (`src/Lad.Web/web/`)

### Новые файлы

| Файл | Что создать |
|------|-------------|
| `src/app/shared/core/workspace-context.service.ts` | Аналог `organization-context.service.ts`. `BehaviorSubject<string \| null>`, хранение в `localStorage` по ключу `currentWorkspaceId`. Методы: `getCurrentWorkspaceId()`, `setCurrentWorkspaceId(id)`, `clearCurrentWorkspace()`. Observable `currentWorkspaceId$` |
| `src/app/shared/interceptors/workspace.interceptor.ts` | Аналог `organization.interceptor.ts`. `HttpInterceptor`, при наличии текущего workspaceId добавляет хедер `X-Workspace-Id` к каждому запросу |
| `src/app/shared/api/workspace-api.service.ts` | Методы: `getList()`, `create(dto)` |
| `src/app/shared/models/workspace.model.ts` | `WorkspaceModel { id?: string; name?: string }` |

### Изменяемые файлы

#### `src/app/shared/models/user.model.ts`
- Заменить `permissionsByOrganization?: { [orgId: string]: number[] }` на `permissionsByWorkspace?: { [wsId: string]: number[] }`

#### `src/app/shared/core/user.service.ts`
- Метод `hasRole(roleId)`: использовать `WorkspaceContextService.getCurrentWorkspaceId()` для выборки прав конкретного workspace, а не перебирать все
- Добавить метод `getWorkspaceIds(): string[]` — возвращает ключи из `permissionsByWorkspace`

#### `src/app/shared/services/permissions.service.ts`
- Инициализация прав (`initPermissions`) — принимать `workspaceId` и загружать только его права
- Либо: при смене workspace перезагружать `_permissions` через `WorkspaceContextService`

#### `src/app/shared/api/auth-api.service.ts`
- После успешного логина маппить `response.permissionsByWorkspace` в `UserModel.permissionsByWorkspace`

#### `src/app/shared/api/users-api.service.ts`
- `getUserRoles(userId, workspaceId)` — заменить имя параметра `organizationId` → `workspaceId`
- `setUserRoles(userId, workspaceId, roles)` — аналогично
- `getProfilesByWorkspace(workspaceId)` — переименовать из `getProfilesByOrganization`

#### `src/app/shared/enums/permission.enum.ts`
- Добавить: `workspaceInfoCrud`, `workspaceMemberCrud`, `workspaceMemberRead` (управление правами участников)

#### `src/app/shared/nav/top-nav/top-nav.component.ts`
- Добавить workspace selector (по аналогии с org selector из asu):
  - Загружать список workspace через `WorkspaceApiService.getList()`
  - Фильтровать по `user.getWorkspaceIds()`
  - При выборе вызывать `WorkspaceContextService.setCurrentWorkspaceId(id)`
  - Если workspace один — selector задизаблен

#### `src/app/role-management/role-management.component.ts`
- Передавать `workspaceId` (из `WorkspaceContextService`) в вызовы `getUserRoles` и `setUserRoles`

#### `src/app/app.providers.ts`
- Добавить `WorkspaceApiService` в `serviceCollection`
- Добавить `workspace.interceptor` в провайдеры HTTP interceptors (в `app.config.ts`)

---

## Порядок реализации

1. **DB / Shared**: `Workspace`, обновить `UserInPermission` + миграция
2. **Backend core**: `CustomClaimTypes`, `HttpContextExtensions`, обновить `AuthService` → `UserDto`
3. **Backend auth**: обновить `AuthController.CreateToken` и `PermissionsValidation`
4. **Backend controllers**: `WorkspaceController` + `WorkspaceService`
5. **Tests**: обновить `UserBuilder` + `SetupApplication`, написать новые тесты
6. **Frontend**: `WorkspaceContextService` → `workspace.interceptor` → обновить `UserModel` + `UserService` → обновить `TopNavComponent` → обновить `RoleManagementComponent`
