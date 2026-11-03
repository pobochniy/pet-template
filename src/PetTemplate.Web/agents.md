---
trigger: always_on
---

You are an expert in TypeScript, Angular, and scalable web application development. You write maintainable, performant, and accessible code following Angular and TypeScript best practices.

Клиент — `src/PetTemplate.Web/web`, проект `pet-template` в `angular.json` (имя `dotnet new`
не переименовывает).

## Angular 21 & Project Structure
- Angular 21, builder `@angular/build` (esbuild). Zone.js нет вовсе — ни в сборке, ни в тестах.
- Standalone APIs only. NgModules (AppModule/SharedModule/feature modules) не используются.
  `standalone: true` не пишем — с Angular 19 это значение по умолчанию.
- Маршруты — только ленивые: раздел подключается в `src/app/app.routes.ts` через `loadChildren`
  или `loadComponent`, экран внутри раздела — `loadComponent` в `{раздел}.routes.ts`. Статический
  импорт компонента в маршруты утаскивает его в initial-бандл (см. «Размер бандла»).
- Переходы между экранами — `withViewTransitions` с `onViewTransitionCreated` (`app.config.ts`):
  внутри одного маршрута (меняется только параметр) перелистывание пропускается, отменённое
  браузером не печатается как ошибка запуска (`isAbortedViewTransition` в `main.ts`).
- **`shared/` — только то, что нужно двум разделам и больше** (или оболочке). Модель формы,
  api-сервис, утилита одного раздела живут в его папке: `profile/profile-api.service.ts`,
  `workspace/form-models/invitation-form.model.ts`, `miniapp/mini-app.util.ts`. Понадобилось
  второму разделу — тогда и переезжает в `shared/`.
- Use signals for state management.
- Do NOT use the `@HostBinding` and `@HostListener` decorators. Put host bindings inside the `host` object of the `@Component` or `@Directive` decorator instead
- Use `NgOptimizedImage` for all static images.
  - `NgOptimizedImage` does not work for inline base64 images.

## UI/UX & Responsive Design
- Bootstrap 5.3 — только стили, `bootstrap.js` не подключён: модалки, выпадашки и сворачивание
  делает Angular (`[class.show]`, сигнал `isOpen`), атрибуты `data-bs-*`/`data-toggle` ничего не делают.
- The UI must be fully responsive and optimized for the following three screen states:
  Phone: portrait orientation.
  Phone: Landscape orientation.
  Tablet: 800x600 pixels.
- Use Bootstrap's grid system and responsive utilities to adapt the layout to different screen sizes.

### Стили: точечный Bootstrap (`src/bootstrap.scss`)

- Вместо `bootstrap.min.css` собирается только то, что есть в шаблонах. Нужен невключённый
  компонент (`navbar`, `spinners`, `modal`, `pagination`…) — раскомментируйте его строку
  в `src/bootstrap.scss`, больше ничего делать не нужно.
- Утилиты (`.mb-3`, `.text-end`, `.bg-warning`) есть все, но **адаптивные варианты** — только
  у групп из `$responsive-utilities` (сейчас `display`: `.d-sm-none`, `.d-md-flex`). Нужен
  `.mb-md-3` или `.text-md-end` — допишите группу туда, иначе класс молча ничего не сделает.
  Сетка (`.col-md-*`, `.offset-md-*`) — не утилита, она есть целиком.
- `@import` вместо `@use` и `silenceDeprecations: ["import"]` в `angular.json` — осознанно:
  Bootstrap 5.3 не модульный (`tech-debt/sass-import.md`).
- **Классы Bootstrap 4 запрещены** — в 5-й версии их нет, и они молча ничего не рисуют:

| Bootstrap 4 | Bootstrap 5 |
|---|---|
| `form-group`, `form-row`, `form-inline` | `mb-3`, `row g-2`, `d-flex flex-wrap align-items-center gap-2` |
| `close` + `&times;` | `btn-close` с `[attr.aria-label]` |
| `ml-*`/`mr-*`/`pl-*`/`pr-*`, `float-left/right`, `text-left/right` | `ms-*`/`me-*`/`ps-*`/`pe-*`, `float-start/end`, `text-start/end` |
| `font-weight-*`, `font-italic` | `fw-*`, `fst-italic` |
| `badge-{цвет}`, `badge-pill` | `text-bg-{цвет}`, `rounded-pill` |
| `custom-select`, `custom-control-*` | `form-select`, `form-check-*` |
| `btn-block`, `no-gutters`, `sr-only`, `media` | `w-100`, `g-0`, `visually-hidden`, `d-flex` |
| `col-xs-*`, `col-md-offset-*`, `input-group-append/prepend` | `col-*`, `offset-md-*`, элементы прямо в `input-group` |

- `<select>` — `form-select`, не `form-control`.
- **Тёмная тема — по системе** (`$color-mode-type: media-query` в `src/bootstrap.scss`). Цвета
  своих стилей — только переменными `--bs-*` (`var(--bs-danger-bg-subtle)`), фон блока —
  `bg-body-tertiary`, а не `bg-light`/`bg-white`: жёсткий цвет в тёмной теме останется светлым.

### Markdown — только `shared/markdown`

- Текст модели, статьи справки и любой недоверенный markdown рисует `<app-markdown [text]>`
  (`shared/markdown`): разбор в дерево узлов и шаблон Angular, без `innerHTML`. Ссылка — только
  на свой маршрут (`routerLink`), чужой адрес остаётся текстом. `[innerHTML]` с данными сервера
  или модели не пишем: за ним осталась бы только CSP.
- Рекурсия в шаблонах — компонентом с атрибутным селектором (`[mdInlines]`), а не
  `ngTemplateOutlet`/`<ng-container>`: их код ложится в initial (`tech-debt/bundle.md`).

### CSP и инлайн-скрипт `index.html`

- nginx отдаёт CSP с `script-src 'self' 'sha256-…'` — хеш единственного инлайн-скрипта
  `src/index.html` (`IaC/nginx/conf/nginx.conf`). Правите скрипт — `npm run csp:hash -- --write`;
  `--check` стоит в `pre-commit`, `web-tests.yml` и после сборки в `publish-*.yml`.
- Инлайн-обработчиков (`onclick="…"`) и новых инлайн-скриптов нет; поэтому в `angular.json`
  выключен `inlineCritical` (его `onload` CSP не пустит).

### Иконки: lucide

- Данные иконок — ванильный пакет `lucide`, рисует их свой `IconComponent`
  (`shared/icons/icon.component.ts`, `<svg [appIcon]>`). Обёртки для Angular (`lucide-angular`,
  `@lucide/angular`), Font Awesome, Bootstrap Icons и CDN не подключаются.
- **Иконка — поштучным импортом** `lucide/dist/esm/icons/<имя>.mjs`, не из `'lucide'`: у пакета
  каждая иконка — свой модуль, и esbuild кладёт её в чанк экрана, который её показывает. Общий вход
  `'lucide'` — barrel на все иконки: взятая через него иконка ленивого экрана уезжает в initial.
  Типы поштучных модулей — `shared/icons/lucide-icons.d.ts`.
- Почему не обёртка: обёртки — один файл (любая иконка из них — в initial рядом с оболочкой),
  у `@lucide/angular` ещё ≈10 КБ ядра и по копии шаблона на каждый `LucideX` (≈3.3 КБ на иконку;
  замер 2026-11-03 — `tech-debt/bundle.md`).
- Иконки компонента — полем `icons`, без `providers` и без имён-строк: опечатка — ошибка сборки.

```ts
import Plus from 'lucide/dist/esm/icons/plus.mjs';
import { IconComponent } from '../../shared/icons/icon.component';

@Component({ imports: [IconComponent] })
export class SomeComponent {
  protected readonly icons = { Plus };
}
```
```html
<svg [appIcon]="icons.Plus" [size]="18"></svg>
```
- Имя файла — kebab-case из каталога https://lucide.dev/icons (`send-horizontal`, `link-2`),
  список — `node_modules/lucide/dist/esm/icons/`. По умолчанию иконка `aria-hidden`: она рядом с
  текстом или в кнопке со своей `aria-label`.

### Размер бандла

- Бюджет `initial` — в `angular.json`, замеры и разбор — `tech-debt/bundle.md`. Сверять по строке
  `Initial total` в выводе `npm run build`. Порог — `maximumError` (замер + 10 КБ): сверх него
  сборка падает. Поднять порог можно только с записью в `tech-debt/bundle.md`, за что.
- В initial лежит только оболочка: меню, шапка, плашки, словарь `core`. Всё остальное — ленивые чанки.
- `@angular/forms` в initial не берём: формы — только на экранах. В оболочке — нативный
  `(change)`/`(input)`, а не `[(ngModel)]` (≈45 КБ).
- Тяжёлое, что нужно не сразу, грузится `import()` по месту.
- Новая зависимость — посмотрите, куда она легла: `npm run build` перечисляет и initial, и lazy чанки.
  CommonJS-пакеты (предупреждение «is not ESM») не берём.

## TypeScript Best Practices

- Use strict type checking: `strict` (вместе с `strictNullChecks`) включён, шаблоны проверяются
  `strictTemplates` и `typeCheckHostBindings`, `module: preserve` (`tsconfig.json`).
- `null`/`undefined` — через `?.` и `??` или проверку с ранним выходом. `!` — только
  с комментарием, почему значение здесь точно есть.
- `tsconfig.app.json` берёт все `src/**/*.ts`, кроме спек: файл, на который нет импорта,
  всё равно проверяется типами.
- Поля класса инициализируются **до** параметров конструктора (`useDefineForClassFields`
  по умолчанию): `readonly id = this.route.snapshot…` в поле не скомпилируется — берите
  `inject(ActivatedRoute)` прямо в инициализаторе или присваивайте в конструкторе.
- Форматирование — настройка `prettier` в `package.json` (`printWidth: 100`, одинарные
  кавычки, парсер `angular` для `*.html`). Сам prettier не ставится — это настройка для IDE;
  форматируйте только тронутые файлы, не репозиторий целиком.
- Prefer type inference when the type is obvious
- Avoid the `any` type; use `unknown` when type is uncertain

## Components

- Keep components small and focused on a single responsibility
- Use `input()` and `output()` functions instead of decorators
- Use `computed()` for derived state
- `changeDetection: ChangeDetectionStrategy.OnPush` — у **каждого** компонента, а всё состояние,
  которое читает шаблон и которое меняется после создания компонента, — `signal()`/`computed()`.
  Приложение без Zone.js: обычное поле, присвоенное после `await`, перерисовку не запросит,
  и экран застынет со старыми данными.
- Сессия реактивна сама: `UserService.profile` и `PermissionsService.permissions` — сигналы,
  `isAuthenticated` читает сигнал профиля. Поэтому `computed(() => …)` и вызовы прямо в шаблоне
  пересчитываются при входе и выходе без подписок.
- Prefer inline templates for small components
- Prefer Reactive forms instead of Template-driven ones
- Do NOT use `ngClass`, use `class` bindings instead
- Do NOT use `ngStyle`, use `style` bindings instead
- Prefer constructor injection in components for services.
  - Correct: `constructor(private newsService: NewsApiService) {}`
  - `inject()` may still be used in factory functions, providers, and tests when constructor DI is not applicable.
  - Перевод берётся полем: `protected readonly i18n = injectI18n(...WORKSPACE_SCOPES);` — им пользуется
    шаблон, а `t` принимает ключи только `core` и словарей маршрута (см. «Язык интерфейса»).

## State Management

- Use signals for local component state
- Use `computed()` for derived state
- Keep state transformations pure and predictable
- Do NOT use `mutate` on signals, use `update` or `set` instead
- Подписки на `Observable` в компоненте — с `takeUntilDestroyed`, иначе они переживают экран.

## Templates

- Keep templates simple and avoid complex logic
- Use native control flow (`@if`, `@for`, `@switch`) instead of `*ngIf`, `*ngFor`, `*ngSwitch`
- Use the async pipe to handle observables
- **Текста в шаблонах нет** — только ключи словаря (см. «Язык интерфейса»).
- Формы типизированы (фабрики, см. «Forms»): контрол в шаблоне — `form.controls.name`, его тип
  проверяет `strictTemplates`. Скобочная запись `form.controls['name']` — только для
  нетипизированного `FormGroup`.

## Forms

- **Модели форм — в `form-models/`** (`*-form.model.ts`): своего раздела (`auth/form-models/`,
  `profile/form-models/`…), а общие двум разделам и больше — в `src/app/shared/form-models/`
  (`login-form.model.ts` нужен входу, приглашению и mini-app).
  - Модель формы — **фабрика**, а не общий экземпляр: `export function createProfileForm() { return new FormGroup({...}); }`,
    в компоненте `public readonly profileForm = createProfileForm();`. Общий `FormGroup` на модуль
    переживал уход с экрана и выход: следующий вошедший видел бы чужие значения.
  - **Never use `FormBuilder` in components.** All forms must be pre-defined in form-models directory.
  - Custom validators should be defined in the form-model file, not in the component.
  - Валидатор формы — сама функция `(control) => ValidationErrors | null`, а не фабрика.
  - Form-models are reusable and can be imported in multiple components if needed.

### Reactive Forms & Validation

- Initialization rules
  - Use the correct `FormControl` constructor: `new FormControl(value, validators?)`.
  - Do NOT pass both value and validators inside a single array. Wrong: `new FormControl(['', [Validators.required]])`.
  - Prefer typed controls for primitives, e.g. `new FormControl<string | null>(null)` when the field can be empty.

- Ошибки поля — общий компонент `form-validation`. Вход — **ключ** подписи поля, тот же, что
  в `<label>`: одна константа на поле и в подписи, и в ошибке.

```html
<label for="password" class="form-label">{{ i18n.t('core.field.password') }}</label>
<input id="password" class="form-control" formControlName="password" />
<form-validation [model]="form.controls.password" fieldKey="core.field.password"></form-validation>
```

  Компонент сам подписан на `events` контрола и просит перерисовку — никаких «невидимых
  async-наблюдателей» в шаблоне не нужно. Серверный отказ 422 у поля встаёт под контрол
  через `applyServerValidationErrors(form, error)` (`shared/core/server-validation.util.ts`):
  сырые `{code, params, text}` лежат в `errors.serverError` и переводятся `form-validation`
  на каждой отрисовке. Возвращает отказы без поля (`payload`) — их экран показывает сам.
- Правила логина, пароля, телефона и почты — `shared/form-models/account-rules.ts`, копия
  `UserNameRule`/`PasswordRule`/`PhoneRule` и `[MaxLength]` почты сервера; ключи ошибок (`userNameFormat`,
  `userNameReserved`, `phoneFormat`) совпадают с хвостами кодов сервера — перевод один.
  Длины и выражения сверяет с C# `npm run enum:parity`.

- UX on submit
  - To reveal errors on submit, call `this.form.markAllAsTouched()` before checking `valid`.
  - Prefer `patchValue` when not all fields are provided; use `setValue` when providing all fields.

## Services & Api calls

- Design services around a single responsibility.
- Use the `providedIn: 'root'` option for singleton services
- All API calls must be contained within services.
- Services must implement methods that return a Promise, allowing them to be consumed by components using async/await.
- Avoid using RxJS. Write code in an async/await style.
- **Чтение — `contextResource`, команды — `async/await`.** Данные экрана, зависящие от
  пространства или пользователя вкладки, читает `contextResource` (`shared/core/context-resource.ts`):
  сменился контекст — прежний запрос обрывается (`abortSignal` → `BaseApiService` → `HttpClient`),
  значение сразу пустое, ответ про прошлое пространство в экран не ляжет. После команды — `reload()`
  (или `set()`, если ответ команды уже есть). Состояние экрана, которое при смене контекста
  сбрасывается (раскрытая строка, подтверждение), — `contextSignal(initial)`. Эффект с флагом
  «первый раз» и `untracked` ради перечитывания — не пишем. `resource()` из `@angular/core`
  не берём: его код ложится в initial (+5 КБ, `tech-debt/bundle.md`), модель у `contextResource` та же.
- Метод api-сервиса, который читает, принимает последним параметром `signal?: AbortSignal`
  и передаёт его в `get/post` `BaseApiService`. `withCredentials` не ставим: вход — `Bearer`,
  запросы того же origin, кук у продукта нет (кука шлюза админки — её дело).
- Api-сервис **только спрашивает сервер**: возвращает DTO и сессию, профиль, активное
  пространство не трогает. Раскладывает ответ экран через `UserService` (см. ниже).
- **Типы DTO — из OpenAPI документа API, а не копией руками.** `npm run api:types`
  (`tools/api-types.mjs`) собирает API, снимает документ `dotnet swagger tofile` (сервер
  не запускается — ни базы, ни Qdrant не нужно) и пишет `shared/api/generated/schema.d.ts`
  (`openapi-typescript`, только типы — в бандл не попадает). Модели в `shared/models/*.ts`
  и `projects/admin/src/app/core/admin.model.ts` — алиасы: `type ProfileModel = ApiSchema<'ProfileDto'>`;
  руками — только то, чего на проводе нет (`ExternalProvider`, признаки запросов `*Filter`).
  Поля ответа обязательны все, `null` — в типе (`email: string | null`); у схемы, которая бывает
  только телом запроса, необязательно то, что сервер примет и пустым. Перечисления в схеме — номера (`0 | 1 | 2`),
  с `enum` клиента совместимы в обе стороны, имена сверяет `enum:parity`. Правите DTO на
  сервере — `npm run api:types` и закоммитить сгенерированное; проверку `--check` гоняют
  `pre-commit` (на `*.cs`/`*.csproj`) и `web-tests.yml`. Ответ сервера в спеке собирается
  фабрикой из `shared/api/dto.mock.ts` (`profileDto`, `sessionDto`, `assistantMessageDto`…):
  новое обязательное поле дописывается туда, а не в каждую спеку.
- Naming for API-layer services: if a service resides under `src/app/shared/api/`, it MUST have the `ApiService` suffix (e.g., `NewsApiService`).
  Api-сервис одного раздела лежит в папке раздела (`workspace/users-api.service.ts`) — с тем же суффиксом.
- Токен в запрос ставит `authInterceptor` (только своему `/api/`), а не api-сервис; он же
  на 401 завершает сессию (`UserService.expireSession`) и уводит на `/auth/login`
  (спец. authorization, 2.1).

### Сессия и активное пространство

- Сессия меняется **в одном месте — `UserService`**. Ответ входа раскладывает
  `applySession(result, activeWorkspaceId?)`, перевыпуск того же человека (пароль, логин,
  подтверждение паролем) — `renewSession(result)` (пространство вкладки остаётся), принятие
  приглашения — `applyAcceptedInvitation(result)` (активным — принятое). Токен, профиль,
  права по пространствам, активное пространство — спец. authorization, 2.1–2.2.
- Токен и пользователь лежат **одной записью** `session` в `localStorage` (`shared/core/session-store.ts`),
  пишутся и читаются целиком. `TokenService` своего кеша не держит — читает запись на каждый запрос.
  Запись **общая у вкладок**, поэтому:
  - соседняя вкладка вошла, вышла или перевыпустила токен — `UserService` узнаёт событием `storage`
    и перечитывает запись, **не записывая в ответ**; сменился человек — разделы убираются
    (`SessionResetService`, `canPersist: false`), открытый экран перепроверяет гарды;
  - 401 стирает запись, только если в ней всё ещё отказанный токен (`expireSession(token)`):
    свежий токен соседней вкладки устаревший запрос этой не сотрёт;
  - `setProfile` не пишет профиль рядом с токеном другого человека: запись чужая — вкладка
    просто перечитывает её.
- Активное пространство — `WorkspaceContextService` (`currentWorkspaceId`), **своё у вкладки**:
  `sessionStorage`, а `localStorage` — умолчание для новой вкладки. Заголовок
  `X-Workspace-Id` ставит `workspaceInterceptor` (только своему `/api/`; уже стоящий заголовок
  не трогает). Переключать — только `UserService.switchWorkspace(id)`: пространство и права
  меняются вместе, `PermissionsService` держит права **активного** пространства.
- `UserService.context` — пользователь и пространство вкладки одним сигналом. От него зависят
  `contextResource` экранов и `recheckRouteAccess` (`route-access.ts`): сменили пространство
  в шапке — открытый экран проходит гарды заново и уводится, если прав больше нет.
  Маршрут с правами объявляется фабрикой `requireRoles(MEMBERS_PAGE_PERMISSIONS)` (`roles.guard.ts`):
  она ставит гард, типизированные `data` и `runGuardsAndResolvers: 'always'`; маршрут
  с `signedInGuard` — тоже с `runGuardsAndResolvers: 'always'`.
- Права не активного пространства — `UserService.permissionsIn(id)` (строка списка своих).
- Экран, который берёт пространство из контекста, перечитывает данные при смене пространства
  в шапке через `contextResource` (образец — `workspace/members`).

### Хранилище браузера — только `safeStorage`

- `localStorage`/`sessionStorage` напрямую не трогаем: в фрейме mini-app VK, приватном режиме
  и при запрете сторонних данных уже обращение к ним бросает. `safeStorage`
  (`shared/core/safe-storage.ts`: `get/set/remove/getJson/setJson`, область `'local' | 'session'`)
  ловит отказ и держит значение в памяти страницы. Это касается и админки.
- Проверяет `npm run lint:storage` (`tools/storage-lint.mjs`, хук и `web-tests.yml`). Не проверяются
  спеки (готовят хранилище напрямую) и инлайн-скрипт `index.html` (идёт до Angular, ловит отказ сам).

### Уборка состояния при выходе из аккаунта

Корневые сервисы переживают выход: следующий пользователь в той же вкладке до первого
ответа сервера видел бы данные предыдущего. Поэтому уборка идёт через реестр
`SessionResetService` (`shared/core/session-reset.service.ts`),
а не прямыми вызовами из кнопки «Выйти». Сама сессия — токен (`TokenService`), профиль
и права по пространствам (`UserService`), активное пространство (`WorkspaceContextService`),
права в нём (`PermissionsService`) — в реестр не пишется: её обнуляет `UserService`.

- Выход — только `UserService.logOut()` (кнопка в шапке зовёт его напрямую) и
  `UserService.expireSession()` (401 в `authInterceptor`). Первый зовёт реестр **до обнуления
  токена**, второй — после. Обнулять профиль, токен и права руками в компоненте нельзя:
  разделы останутся с данными прошлого пользователя.
- Сервис с состоянием, переживающим смену пользователя (кеш списка, треды ассистента), записывается сам — `inject(SessionResetService).register(() => this.reset())`
  в конструкторе. Так уборка достаётся только тем сервисам, которые в этом заходе действительно создавали.
- Обработчик обязан привести состояние в порядок и при неудачном запросе: ошибка
  одного не отменяет остальных и не срывает сам выход. `canPersist: false` — токен уже
  не принят, на сервер не ходить.

### Service Providers

- **Global provider configuration**: сервисы без `providedIn: 'root'` перечислены в
  `src/app/app.providers.ts` (`serviceCollection`), он подключён в `appConfig`.
- **Never use local providers**: не добавляйте `providers: [SomeService]` в декоратор компонента —
  это второй экземпляр мимо общей конфигурации.
- Интерцепторы — функции (`HttpInterceptorFn`) в `withInterceptors([...])`, а не классы.
- **Подмены под запуск без бэкенда нет.** Раньше `environment.useMockApi` подставлял моки
  в сборку; убран: моки расходились с сервером и молча показывали не то, что покажет
  прод, а проверка поведения и так лежит на тестах. Посмотреть экран руками — поднимается
  настоящий бэкенд, дев-сервер ходит в него через `proxy.conf.json`.

### Моки живут только в спеках

Мок — это подмена в `TestBed`, а не режим сборки. Две формы, обе законные:

- **`*.mock.ts` рядом с сервисом** — когда подмену просят **две и более** спеки **или** ей нужна
  память между вызовами. У такого сервиса заводится интерфейс, и мок его реализует — иначе
  подмена разъедется с настоящим сервисом молча.
- **Подмена прямо в файле спеки** — когда она нужна одному экрану: класс или объект
  из `vi.fn()` (`{ view: vi.fn() } as unknown as Mocked<ProfileApiService>`) с ответом,
  заданным в самом тесте (`mockResolvedValue(...)`).

Правила моков:

- Мок повторяет **правила сервера**, а не только форму ответа. Тогда зелёная спека значит
  «так и работает», а не «подмена промолчала».
- `vi.fn()` без заданного ответа возвращает `undefined`, и экран «работает» на пустоте — ответ
  задаётся явно в каждом тесте, где его читают. `vi.spyOn` же, в отличие от Jasmine, **зовёт
  настоящий метод**: заглушить — `.mockImplementation(() => {})` или `.mockResolvedValue(...)`.
  Шпионы `vi.spyOn` снимаются после каждого теста (`src/test-env.ts`).
- Без `console.log` и искусственных задержек: они только замедляют прогон.
- Состояние клиента (`UserService`, `PermissionsService`, `TokenService`) берётся настоящее
  из `TestBed` и заполняется в тесте (`applySession`, `setProfile`, `initPermissions`). Другую
  вкладку изображает запись в `localStorage` и `window.dispatchEvent(new StorageEvent('storage', …))`.

## Отказы сервера

- `HttpErrorResponse.message` — это всегда «Http failure response for …», показывать его нельзя.
- Сервер отвечает на отказ `{ error, statusCode, code?, params? }` (`ErrorMessage(code, text)`
  на сервере). Показ — `serverErrorMessage(error, i18n, fallback)` из `shared/core/server-error.util.ts`:
  перевод кода, иначе текст сервера, иначе своя фраза. Код ищется в словаре по первому
  сегменту — словарь с кодами раздела грузит его маршрут (`'invite'` в `WORKSPACE_SCOPES`);
  `npm run i18n:codes` сверяет, что каждый код сервера есть в словаре.
- 422 у поля — `{поле: [{code, params, text}]}`, имя поля в camelCase. У формы —
  `applyServerValidationErrors` (см. «Forms»); где поле не контрол (галки прав) —
  `serverFieldErrors(error, 'permissions', i18n)`. Моки отказывают `serverRefusal`/`validationRefusal`
  (`shared/api/server-refusal.mock.ts`), а не голым `Error`.
- **Вход** — «Неверный логин или пароль» своей фразой всегда: подробности превратили бы форму
  в подтверждатель чужих логинов.

## Alerts (Global notifications)

- Service: `src/app/shared/alerts/alerts.service.ts` (`providedIn: 'root'`), сигнал `alerts`.
- Component: `<shared-alerts>` — один раз в `app.html`.
- API:
  - `push(alertClass, content, timeToCloseMs = 0)` — `content` уже переведённый текст;
  - `remove(id)`.
- Закрытие — через `(click)` → `remove`, не `data-bs-dismiss`.

## 🌍 Язык интерфейса (i18n)

### Quick Reference

- **Словари:** `i18n/{lang}/{scope}.json` (рядом с `src/`, в `src/PetTemplate.Web/web/i18n`) —
  правятся руками, `en` эталон полноты
- **Кодоген:** `npm run i18n` → `src/app/shared/i18n/generated/` (сам запускается в `prestart`,
  `prebuild`, `pretest`); `npm run i18n:check` сверяет, что сгенерированное не отстало.
  Сгенерированное коммитится.
- **Движок:** `shared/i18n/i18n.service.ts` (`t`, `plural`, `translateCode`, `loadScope`, `setLang`)
- **Язык:** `shared/i18n/lang.ts` — тип `Lang`, список языков, порядок определения
  (`localStorage` → браузер → `ru`); переключатель — флаг в шапке + `shared/language-picker`.
  Язык профиля на сервере (`Profile.Language`, спец. accounts, 8.4) — `shared/i18n/language.service.ts`:
  `persist` молча сохраняет выбор из шапки (сервис грузится `import()`), `adopt` переключает
  интерфейс на язык профиля после входа и сброса пароля; регистрация шлёт `i18n.lang()`
- **Форматы:** `shared/i18n/format.util.ts` и `formatters.ts` (кеш `Intl.*`)
- **Защита:** `npm run i18n:lint` (текст мимо словаря; разбор — компиляторы TypeScript
  и Angular, самопроверка на образцах — `npm run test:tools`), `npm run i18n:codes` (коды сервера
  есть в словаре), `i18n-parity.spec.ts` (одинаковые ключи и подстановки во всех языках)

### Нового текста в коде не бывает — только ключи

Это правило, а не пожелание: его проверяет `npm run i18n:lint` (текстовые узлы шаблонов,
атрибуты `title`/`aria-label`/`placeholder`/`alt` и строковые литералы с кириллицей в `*.ts`).

```html
<!-- Правильно -->
<button [title]="i18n.t('core.action.save')">{{ i18n.t('core.action.save') }}</button>

<!-- Неправильно -->
<button title="Сохранить">Сохранить</button>
```

- **Ключ — `{scope}.{блок}.{элемент}`**, первый сегмент равен имени словаря. Ключи типизированы:
  опечатка — ошибка компиляции.
- **Общее лежит в `core`**, своё — в словаре раздела. Подписи, которые рисуют два разных раздела,
  — только `core`: словарь соседнего раздела в этом чанке не загружен.
- **Константа отдаёт ключ, а не текст.** Модуль читается один раз, и готовая строка застряла бы
  на языке того момента. Образец: `PERMISSION_CATALOG` в `workspace/permission-catalog.ts` (`labelKey`, `groupKey` — ключи
  типа `ScopedKey<'workspace'>`).
- Строки с числом — `i18n.plural(key, n)`, формы `one/other` (en) и `one/few/many/other` (ru).
- **Логи и `console.*` — английские.**
- Пользовательское содержимое не переводится: имена, комментарии.
- Бренды, которые проверка не трогает, — `BRANDS` в `tools/i18n-lint.mjs` (`PetTemplate`, `Telegram`).
- Литерал в переводимом атрибуте отбивается на любом языке: `title="Save"` так же, как
  `title="Сохранить"`, и в любой записи — `[title]="'…'"`, `[attr.aria-label]="'…'"`, ветви
  `cond ? '…' : '…'`. Значение, которое человеку не показывают (`alt` картинки под `aria-hidden`), —
  строкой в `ALLOWED_ATTRS` того же скрипта: атрибут, точное значение и причина; ключ на слово,
  которого никто не увидит, не заводят.

### Словарь раздела едет со своим чанком

В `app.routes.ts` раздел подключается через `withScope(PROFILE_SCOPES, import('./profile/profile.routes'))`:
словари и чанк грузятся одним `Promise.all`. Резолвером так делать нельзя — фаза `RESOLVE`
идёт до загрузки компонентов, и словарь грузился бы перед чанком, а не вместе с ним.
Новый раздел = новая папка + `i18n/{en,ru}/{scope}.json` + `{раздел}/{раздел}.scopes.ts` + строка в `app.routes.ts`.

**Словари маршрута проверяются на компиляции.** `I18nKey` — ключи всех словарей, и `t('invite.x')`
скомпилировался бы на экране, чей маршрут `invite` не грузит. Поэтому список словарей маршрута —
одна константа в `{раздел}/{раздел}.scopes.ts` (`export const PROFILE_SCOPES = ['profile', 'auth'] as const`),
её грузит `withScope` и её же принимает экран:

```ts
protected readonly i18n = injectI18n(...PROFILE_SCOPES);   // t: только core.*, profile.*, auth.*
```

`injectI18n()` без аргументов — только `core` (оболочка, общие компоненты). Типы ключей по словарям
(`ScopeKeys`, `ScopedKey<S>`) генерирует кодоген. `inject(I18nService)` с полным `I18nKey` — только
там, где ключ приходит снаружи (`form-validation`). `npm run i18n:codes` читает константы `*.scopes.ts`.

В юнит-тестах маршрута нет — словари грузит `loadAllScopes` из `shared/i18n/i18n.testing.ts`,
а язык закреплён русским в `src/test-setup.ts` (`setupFiles` в `angular.json`).

### Даты, числа и сортировка — мимо `DatePipe`/`DecimalPipe`

Они берут локаль из `LOCALE_ID`, а тот неизменяем после старта: переключение языка на лету его
не заденет. Поэтому `formatDate`, `formatNumber`, `compareStrings`, `weekdayNames` из
`shared/i18n/format.util.ts` — язык передаётся параметром (`i18n.lang()`).
Названия дней и месяцев в словарях не держим — их отдаёт `Intl`.

## Testing

Angular 21, Vitest (`@angular/build:unit-test`, `runner: vitest`) в jsdom — без браузера,
без Zone.js. Своего `vitest.config` нет — всё в `angular.json`. Общая настройка — `setupFiles`:
`src/test-env.ts` (оба проекта: шпионы снимаются после теста, хранилище jsdom на Node 25,
заглушка `scrollIntoView`) и `src/test-setup.ts` (продукт: русский язык и чистая сессия
перед каждым тестом). Глобальные `describe`/`it`/`expect`/`vi` — из `vitest/globals`
(`tsconfig.spec.json`), импортировать их не нужно; типы `Mock`, `Mocked` —
`import type { Mocked } from 'vitest'`.

- Раскладки в jsdom нет: размеры, `IntersectionObserver`, прокрутка и `matchMedia` не
  работают — проверяйте классы и состояние, а не геометрию. Таймеры — `vi.useFakeTimers()`
  и `vi.advanceTimersByTime(...)`, вернуть — `vi.useRealTimers()` в `finally`/`afterEach`.
- Файлы спек одного воркера делят модули и окружение (`isolate: false` — по умолчанию
  у Angular, как было в Karma): состояние на уровне модуля чистите сами.
- Отказ навигации, оставленный тестом (`void router.navigateByUrl('/login')` без маршрута),
  Vitest считает необработанной ошибкой и роняет прогон — даже при зелёных тестах. Дайте
  маршрут (`provideRouter([{ path: 'login', children: [] }])`) или подмените переход.

### Zoneless configuration (important)
- В каждой спеке, создающей компоненты: `providers: [provideZonelessChangeDetection()]`.
- Перерисовка: `await fixture.whenStable()` после изменений и асинхронщины
  (`fixture.detectChanges()` — когда нужен синхронный проход).

### Test naming convention
- `should <expectation> [when <condition>]`: `should show profile and log out to a signed-in user`.

### Mocks & DI (domain-oriented)
- Подмена объявляется провайдером в самой спеке (см. «Моки живут только в спеках»).
  Пара `useClass` + `useExisting`, чтобы достать общий мок и настроить или проверить его состояние:

```ts
providers: [
  { provide: SomeApiService, useClass: SomeApiServiceMock },
  { provide: SomeApiServiceMock, useExisting: SomeApiService },
  provideZonelessChangeDetection()
]
```

- Проверяйте состояние, а не только вызов: «плашка показана», «профиль в сессии», «пункт отмечен».
- **Ответ мока, пришедший после клика.** Мок, отвечающий мгновенно, даёт перерисоваться
  от самого клика — и спека проходит даже на экране без сигналов. Хотя бы в одном тесте экрана
  проверяйте разметку после асинхронного ответа: такой тест падает, если состояние не в сигнале.
- Проверяйте то, что видит человек: разметку и переведённый текст. Язык в тестах — русский;
  проверка перевода переключает его сама: `await TestBed.inject(I18nService).setLang('en')`.

### Router testing
- Use `provideRouter([...])`. Для `<a routerLink>` проверяйте `href`.
- Маршруты целиком (словарь едет с чанком, guard) — `RouterTestingHarness` (`app.routes.spec.ts`).

### Testing responsive markup
- Unit tests do not change the real viewport. Assert Bootstrap utility classes (`d-none d-sm-block`).
- Prefer stable test selectors (IDs) on interactive elements.

### How to run tests

```bash
npx ng test pet-template --watch=false                                                  # все
npx ng test pet-template --watch=false --include=src/app/shared/nav/top-nav/top-nav.component.spec.ts  # одну спеку
npx ng test pet-template --watch=false --filter='TopNavComponent'                       # по имени describe/it
```

Покрытия нет: `--coverage` требует `@vitest/coverage-v8`, а с `isolate: false` Angular
`setupFiles` выполняются раз на воркер, и тесты падают. Понадобится — пакет и `--runner-config`
с `test: { isolate: true }` (прогон вдвое дольше).

Проект назван явно: в `angular.json` их два, второй — админка (раздел «Админка»).

`ng test`, а не `npm test`: тот без `--watch=false` уходит в watch и ждёт правок.

### Перед коммитом

Хуки `.githooks/` (включаются один раз на клон: `git config core.hooksPath .githooks`):
кодоген, линт и коды — `pre-commit`, спеки — `pre-push`. Те же проверки — ворота
выкатки: `web-tests.yml` (спеки, i18n) перед `publish-angular.yml`, а `npm run build`
с бюджетом `initial` в `angular.json` — `maximumError`, раздутый бандл **роняет сборку**
и не выкатывается. Со включёнными хуками первые две строки ниже делать руками незачем —
хуки сделают их сами и только для тронутого. `npm run build` в хуках нет (бюджет бандла
сторожит `publish-angular.yml`), его — руками. Без хуков (не включены на клоне,
`--no-verify`) — всё то же, что скажут хуки и деплой:

```bash
npm run i18n:check && npm run i18n:lint && npm run i18n:codes && npm run lint:storage && npm run enum:parity && npm run csp:hash -- --check
npm run test:tools && npm run api:types -- --check
npx ng test pet-template --watch=false
npm run build        # сверить Initial total с tech-debt/bundle.md
```

## 🛠 Админка (`projects/admin`)

Отдельный сайт `adminka.example.com` — второй проект `admin` в том же `angular.json`
(`specs/admin.md`, разделы 2 и 4). Своя точка входа `projects/admin/src/main.ts`,
префикс `adm`, свой `ng build admin` и `ng test admin`; общие с продуктом только
зависимости и, при нужде, код из `src/app/shared` по импорту. Продукт поэтому называется
явно — `ng test pet-template`, `ng build pet-template` (`npm test`/`npm run build` уже с именем).

```bash
npm run start:admin   # дев-сервер на :4201 (его пускает CORS API), тот же proxy.conf.json
npm run test:admin    # спеки админки, Vitest в jsdom
npm run build:admin   # сборка со своим бюджетом
```

**Что на неё не распространяется — и почему.** Интерфейс на одного-двух человек,
вынесенный отдельно ровно затем, чтобы не объявлять исключений внутри продукта (спец. admin, 2):

- **i18n** — словарей и `t()` нет, русские строки в шаблонах и `*.ts` законны. `i18n:lint`,
  `i18n:check` и хуки смотрят только в `src/`, `i18n`, `tools` — `projects/` не читают.
  Кодов отказов админка не заводит: ошибка сервера показывается текстом как есть (`apiErrorText`);
- **адаптив** — только десктоп, таблицы и формы на полном `bootstrap.min.css`, без своего дизайна
  и без поштучного `src/bootstrap.scss`;
- **`form-validation`, иконки, workspace-интерцептор** — не используются: формы
  на `FormsModule`, интерцептор один (`adminInterceptor`, только `Authorization`, без
  `X-Workspace-Id` — workspace у админских запросов нет, спец. admin, 3.5);
- **бюджет продукта** — у админки свой `initial`, ошибка на 600 КБ: больше половины —
  `bootstrap.min.css` целиком (спец. admin, 4);
- **`pre-push`** — спеки админки на пуш не вешаются (правится редко); воротами выкатки они
  идут в `web-tests.yml` вместе с продуктовыми — и для `publish-angular.yml`, и для
  `publish-admin.yml`.

Остальное — как у продукта: Angular 21, zoneless, standalone, `OnPush` и сигналы, спеки
по правилам раздела «Testing».

**Устройство.** Токен своей схемы в `localStorage` поддомена, срок отсчитывает клиент
(`AdminSessionService`; продления нет). `adminGuard` пускает с живым токеном и после
перезагрузки спрашивает `Admin/Me`: `401`/`404` кончают сессию, сеть и `5xx` — нет.
Разделы и их роли — `shell/sections.ts`, экраны — ленивыми чанками в `app.routes.ts`
(`SCREENS`); раздел без экрана открывается заголовком (`SectionComponent`), без роли —
не открывается и по прямой ссылке (`roleGuard`). Страница приглашения `invite/:token` —
вне оболочки и без `adminGuard`: у пришедшего по ссылке ни ролей, ни сессии (спец. admin, 3.4).
Роли — копия `AdminRole` из
`src/PetTemplate.Shared/Enums/AdminRole.cs` с теми же номерами в `core/admin-role.ts` (статус
приглашения — так же, `core/admin-invitation-status.ts`); номера и имена всех таких копий
(и продукта, и админки) сверяет с C# `npm run enum:parity` — новое перечисление дописывается
в его таблицу `ENUMS`. DTO админки — алиасы того же `schema.d.ts` продукта (документ у API
один, `npm run api:types`); правите DTO — помните, что сборок две. Токен админки — тоже через `safeStorage`.
