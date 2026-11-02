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

### Иконки: lucide

- `lucide-angular`; Font Awesome, Bootstrap Icons и CDN не подключаются.
- Иконки регистрируются **в `providers` компонента** через `provideIcons({...})`
  из `shared/icons/lucide-icons.provider.ts`. Это законное исключение из правила «не объявлять
  providers» (см. ниже) — провайдер несёт данные, а не подменяет реализацию сервиса.
- **Иконки ленивого экрана — из `'lucide-angular/src/icons'`**, компонент — из `'lucide-angular'`.
  Пакет — один файл, esbuild режет по файлам: иконка, взятая из `'lucide-angular'` где угодно,
  ложится в чанк initial рядом с компонентом оболочки, сколько бы её ни регистрировали в экране
  (`tech-debt/lucide-icons-entry.md`). Исключение — иконки, которые уже показывает оболочка
  (`Users`, `LogOut`…): их берите из `'lucide-angular'`, второй копии данных не нужно.
  Предупреждение `NG0912` в тестах и `ng serve` — следствие этого правила, оно безвредно.

```ts
import { LucideAngularModule } from 'lucide-angular';
import { Plus } from 'lucide-angular/src/icons';
import { provideIcons } from '../../shared/icons/lucide-icons.provider';

@Component({
  imports: [LucideAngularModule],
  providers: [provideIcons({ Plus })]
})
```
```html
<i-lucide name="plus"></i-lucide>
```
- Имена — kebab-case, список — `node_modules/lucide-angular/icons/*.d.ts`.

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
  - `I18nService` берётся полем: `protected readonly i18n = inject(I18nService);` — им пользуется шаблон.

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

- **Reactive Forms models must be defined in `src/app/shared/form-models/`** (`*-form.model.ts`).
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
  `UserNameRule`/`PasswordRule`/`PhoneRule` сервера; ключи ошибок (`userNameFormat`,
  `userNameReserved`, `phoneFormat`) совпадают с хвостами кодов сервера — перевод один.

- UX on submit
  - To reveal errors on submit, call `this.form.markAllAsTouched()` before checking `valid`.
  - Prefer `patchValue` when not all fields are provided; use `setValue` when providing all fields.

## Services & Api calls

- Design services around a single responsibility.
- Use the `providedIn: 'root'` option for singleton services
- All API calls must be contained within services.
- Services must implement methods that return a Promise, allowing them to be consumed by components using async/await.
- Avoid using RxJS. Write code in an async/await style.
- Naming for API-layer services: if a service resides under `src/app/shared/api/`, it MUST have the `ApiService` suffix (e.g., `NewsApiService`).
- Токен в запрос ставит `authInterceptor` (только своему `/api/`), а не api-сервис; он же
  на 401 завершает сессию (`UserService.expireSession`) и уводит на `/auth/login`
  (спец. authorization, 2.1).

### Сессия и активное пространство

- Ответ входа и любого перевыпуска сессии раскладывает только `UserService.applySession(result,
  activeWorkspaceId?)`: токен, профиль, права по пространствам, активное пространство
  (спец. authorization, 2.1–2.2). Пользователь сессии лежит в `localStorage` (`user`) и
  переживает перезагрузку.
- Активное пространство — `WorkspaceContextService` (`currentWorkspaceId`), заголовок
  `X-Workspace-Id` ставит `workspaceInterceptor` (только своему `/api/`; уже стоящий заголовок
  не трогает). Переключать — только `UserService.switchWorkspace(id)`: пространство и права
  меняются вместе, `PermissionsService` держит права **активного** пространства.
- Права не активного пространства — `UserService.permissionsIn(id)` (строка списка своих).
- Экран, который берёт пространство из контекста, перечитывает данные при смене пространства
  в шапке (образец — `workspace/members`).

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
  это второй экземпляр мимо общей конфигурации. Исключение — `provideIcons(...)` (см. «Иконки»).
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
- **Подмена прямо в файле спеки** — когда она нужна одному экрану: класс или
  `jasmine.createSpyObj` с ответом, заданным в самом тесте (`and.resolveTo(...)`).

Правила моков:

- Мок повторяет **правила сервера**, а не только форму ответа. Тогда зелёная спека значит
  «так и работает», а не «подмена промолчала».
- Спай без заданного ответа возвращает `undefined`, и экран «работает» на пустоте — ответ
  задаётся явно в каждом тесте, где его читают.
- Без `console.log` и искусственных задержек: они только замедляют прогон.
- Состояние клиента (`UserService`, `PermissionsService`, `TokenService`) берётся настоящее
  из `TestBed` и заполняется в тесте (`setProfile`, `initPermissions`).

## Отказы сервера

- `HttpErrorResponse.message` — это всегда «Http failure response for …», показывать его нельзя.
- Сервер отвечает на отказ `{ error, statusCode, code?, params? }` (`ErrorMessage(code, text)`
  на сервере). Показ — `serverErrorMessage(error, i18n, fallback)` из `shared/core/server-error.util.ts`:
  перевод кода, иначе текст сервера, иначе своя фраза. Код ищется в словаре по первому
  сегменту — словарь с кодами раздела грузит его маршрут (`withScope(..., 'invite')`);
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
- **Защита:** `npm run i18n:lint` (текст мимо словаря), `npm run i18n:codes` (коды сервера есть
  в словаре), `i18n-parity.spec.ts` (одинаковые ключи и подстановки во всех языках)

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
  на языке того момента. Образец: `PERMISSION_CATALOG` в `shared/permissions/permission-catalog.ts` (`labelKey`, `groupKey` — ключи).
- Строки с числом — `i18n.plural(key, n)`, формы `one/other` (en) и `one/few/many/other` (ru).
- **Логи и `console.*` — английские.**
- Пользовательское содержимое не переводится: имена, комментарии.
- Бренды, которые проверка не трогает, — `BRANDS` в `tools/i18n-lint.mjs` (`PetTemplate`, `Telegram`).

### Словарь раздела едет со своим чанком

В `app.routes.ts` раздел подключается через `withScope('profile', import('./profile/profile.routes'))`:
словарь и чанк грузятся одним `Promise.all`. Резолвером так делать нельзя — фаза `RESOLVE`
идёт до загрузки компонентов, и словарь грузился бы перед чанком, а не вместе с ним.
Новый раздел = новая папка + `i18n/{en,ru}/{scope}.json` + строка в `app.routes.ts`.

В юнит-тестах маршрута нет — словари грузит `loadAllScopes` из `shared/i18n/i18n.testing.ts`,
а язык закреплён русским в `src/test-setup.spec.ts`.

### Даты, числа и сортировка — мимо `DatePipe`/`DecimalPipe`

Они берут локаль из `LOCALE_ID`, а тот неизменяем после старта: переключение языка на лету его
не заденет. Поэтому `formatDate`, `formatNumber`, `compareStrings`, `weekdayNames` из
`shared/i18n/format.util.ts` — язык передаётся параметром (`i18n.lang()`).
Названия дней и месяцев в словарях не держим — их отдаёт `Intl`.

## Testing

Angular 21, Karma + Jasmine (`@angular/build:karma`), ChromeHeadless, без Zone.js.
Отдельного `test.ts` и `karma.conf.js` нет — общая настройка в `src/test-setup.spec.ts`.

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
npx ng test pet-template --watch=false --browsers=ChromeHeadless                       # все
# одну спеку — обязательно вместе с test-setup, иначе язык будет английским и русские проверки упадут
npx ng test pet-template --watch=false --browsers=ChromeHeadless --include=src/test-setup.spec.ts --include=src/app/shared/nav/top-nav/top-nav.component.spec.ts
npx ng test pet-template --code-coverage --browsers=ChromeHeadless --watch=false        # покрытие → coverage/
```

Проект назван явно: в `angular.json` их два, второй — админка (раздел «Админка»).

`ng test`, а не `npm test`: тот без флагов уходит в watch и открывает обычный Chrome.

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
npm run i18n:check && npm run i18n:lint && npm run i18n:codes
npx ng test pet-template --watch=false --browsers=ChromeHeadless
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
npm run test:admin    # спеки админки, headless
npm run build:admin   # сборка со своим бюджетом
```

**Что на неё не распространяется — и почему.** Интерфейс на одного-двух человек,
вынесенный отдельно ровно затем, чтобы не объявлять исключений внутри продукта (спец. admin, 2):

- **i18n** — словарей и `t()` нет, русские строки в шаблонах и `*.ts` законны. `i18n:lint`,
  `i18n:check` и хуки смотрят только в `src/`, `i18n`, `tools` — `projects/` не читают.
  Кодов отказов админка не заводит: ошибка сервера показывается текстом как есть (`apiErrorText`);
- **адаптив** — только десктоп, таблицы и формы на полном `bootstrap.min.css`, без своего дизайна
  и без поштучного `src/bootstrap.scss`;
- **`form-validation`, `provideIcons`, workspace-интерцептор** — не используются: формы
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
приглашения — так же, `core/admin-invitation-status.ts`); правите
DTO админки — помните, что сборок две.
