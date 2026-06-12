---
trigger: always_on
---

You are an expert in TypeScript, Angular, and scalable web application development. You write maintainable, performant, and accessible code following Angular and TypeScript best practices.

## Angular 20 & Project Structure
- Use Angular 20.
- Use standalone Angular APIs only. No NgModules (AppModule/SharedModule/feature modules) are used in this project.
- Configure routing with standalone route definitions. Use lazy loading via `loadComponent`/`loadChildren` to optimize initial load times.
- Must set `standalone: true` inside Angular decorators for components, directives, and pipes.
- Use signals for state management
- Implement lazy loading for feature routes
- Do NOT use the `@HostBinding` and `@HostListener` decorators. Put host bindings inside the `host` object of the `@Component` or `@Directive` decorator instead
- Use `NgOptimizedImage` for all static images.
  - `NgOptimizedImage` does not work for inline base64 images.

## UI/UX & Responsive Design
- Use Bootstrap 5 for styling and UI components.
- The UI must be fully responsive and optimized for the following three screen states:
  Phone: portrait orientation.
  Phone: Landscape orientation.
  Tablet: 800x600 pixels.
- Use Bootstrap's grid system and responsive utilities to adapt the layout to different screen sizes.


## TypeScript Best Practices

- Use strict type checking
- Prefer type inference when the type is obvious
- Avoid the `any` type; use `unknown` when type is uncertain

## Components

- Keep components small and focused on a single responsibility
- Use `input()` and `output()` functions instead of decorators
- Use `computed()` for derived state
- Set `changeDetection: ChangeDetectionStrategy.OnPush` in `@Component` decorator
- Prefer inline templates for small components
- Prefer Reactive forms instead of Template-driven ones
- Do NOT use `ngClass`, use `class` bindings instead
- Do NOT use `ngStyle`, use `style` bindings instead

- Prefer constructor injection in components for services.
  - Correct: `constructor(private newsService: NewsApiService) {}`
  - `inject()` may still be used in factory functions, providers, and tests when constructor DI is not applicable.

## State Management

- Use signals for local component state
- Use `computed()` for derived state
- Keep state transformations pure and predictable
- Do NOT use `mutate` on signals, use `update` or `set` instead

## Templates

- Keep templates simple and avoid complex logic
- Use native control flow (`@if`, `@for`, `@switch`) instead of `*ngIf`, `*ngFor`, `*ngSwitch`
- Use the async pipe to handle observables
- Strict template type checking with Reactive Forms: when accessing controls in templates, use bracket notation instead of dot notation. Example: expose `get f() { return this.form.controls; }` in the component and use `f['alias']` (not `f.alias`) in the template. This avoids type errors in strict mode.

```html
<!-- Correct -->
<input formControlName="alias" [class.is-invalid]="f['alias'].touched && f['alias'].invalid" />

<!-- Incorrect -->
<!-- [class.is-invalid]="f.alias.touched && f.alias.invalid" -->
```

## Forms

- **Reactive Forms models must be defined in `src/app/shared/form-models/` and exported with the `FormModel` suffix.**
  - Example path: `src/app/shared/form-models/epic-form.model.ts`
  - Example export name: `export const epicFormModel = new FormGroup({ ... });`
  - Usage in component: `public epicForm: FormGroup = epicFormModel;`
  - **Never use `FormBuilder` in components.** All forms must be pre-defined in form-models directory.
  - Custom validators should be defined in the form-model file, not in the component.
  - Form-models are reusable and can be imported in multiple components if needed.

### Reactive Forms & Validation

- Initialization rules
  - Use the correct `FormControl` constructor: `new FormControl(value, validators?)`.
  - Do NOT pass both value and validators inside a single array. Wrong: `new FormControl(['', [Validators.required]])`.
  - Prefer typed controls for primitives, e.g. `new FormControl<string | null>(null)` when the field can be empty.

```ts
import { FormGroup, FormControl, Validators } from '@angular/forms';

export const exampleFormModel: FormGroup = new FormGroup({
  alias: new FormControl('', [Validators.required, Validators.minLength(3), Validators.maxLength(100)]),
  id: new FormControl<string | null>(null),
  tags: new FormControl<string | null>(null, [Validators.maxLength(100)]),
});
```

- Template access and classes
  - Access controls with bracket notation under strict typing: expose `get f() { return this.form.controls; }` and use `f['alias']`.
  - Recommended invalid class binding:

```html
<input formControlName="alias" [class.is-invalid]="f['alias'].invalid && (f['alias'].touched || f['alias'].dirty)" />
```

- Error UI component (shared)
  - Use a shared component to render field errors: `form-validation` with `@Input() model: AbstractControl` and `@Input() fieldName: string`.
  - Usage in template: `<form-validation [model]="formControls['fieldName']" fieldName="Field Name"></form-validation>`
  - **Important:** Expose form controls via getter to avoid type errors: `get formControls() { return this.form.controls; }`
  - Because we use `ChangeDetectionStrategy.OnPush`, ensure the error UI reacts to input changes without Zone.js by adding invisible async watchers in the template:

```html
@if (model?.statusChanges | async) {}
@if (model?.valueChanges | async) {}

@if (model && model.errors && (model.touched || model.dirty)) {
  <div class="error-msg">
    @if (model.errors['required']) { <div class="text-danger">Поле "{{fieldName}}" обязательно</div> }
    @if (model.errors['minlength']) {
      <div class="text-danger">
        Минимальная длина {{ model.errors['minlength'].requiredLength }}. Сейчас: {{ model.errors['minlength'].actualLength }}
      </div>
    }
    @if (model.errors['maxlength']) { <div class="text-danger">Максимальная длина {{ model.errors['maxlength'].requiredLength }}</div> }
  </div>
}
```

- UX on submit
  - To reveal errors on submit, call `this.form.markAllAsTouched()` when the form is invalid.
  - Prefer `patchValue` when not all fields are provided; use `setValue` when providing all fields.

- Testing (zoneless + OnPush)
  - Provide `provideZonelessChangeDetection()` in specs.
  - Manually trigger change detection with `fixture.detectChanges()` after input events.
  - The async watchers above ensure error UI updates without relying on Zone.js.

## Services & Api calls

- Design services around a single responsibility
- Use the `providedIn: 'root'` option for singleton services
- Use the `inject()` function instead of constructor injection in non-component contexts if needed (e.g., provider factories). In components, prefer constructor injection (see Components section).
- Design services around a single responsibility.
- All API calls must be contained within services.
- Services must implement methods that return a Promise, allowing them to be consumed by components using async/await.
- Avoid using RxJS. Write code in an async/await style.
- Naming for API-layer services: if a service resides under `src/shared/api/`, it MUST have the `ApiService` suffix (e.g., `NewsApiService`). Avoid generic names like `NewsService`.

### Service Providers & Mock API Pattern

**CRITICAL: Never override providers in component decorators**

- **Global provider configuration**: All service providers must be configured in `src/app/app.providers.ts` and included in `appConfig` (in `app.config.ts`).
- **Never use local providers**: Do NOT add `providers: [SomeService]` in component decorators. This creates a new instance and bypasses the global configuration.
- **Mock API pattern**: Use `environment.useMockApi` flag to switch between real and mock implementations:

```ts
// src/app/app.providers.ts
import { AuthApiService } from './shared/api/auth-api.service';
import { AuthApiServiceMock } from './shared/api/auth-api.service.mock';
import { environment } from '../environments/environment';

export const serviceCollection = [
  { 
    provide: AuthApiService, 
    useClass: environment.useMockApi ? AuthApiServiceMock : AuthApiService 
  },
  // other services...
];
```

```ts
// src/app/app.config.ts
import { serviceCollection } from './app.providers';

export const appConfig: ApplicationConfig = {
  providers: [
    provideExperimentalZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding(), withViewTransitions()),
    provideHttpClient(withInterceptorsFromDi()),
    serviceCollection  // ← Global providers
  ]
};
```

- **Service interfaces**: Define interfaces for services that have mock implementations:

```ts
// src/shared/api/auth-api.service.ts
export interface IAuthApiService {
  register(model: FormGroup): Promise<UserModel>;
  login(model: FormGroup): Promise<UserModel>;
  logOut(): Promise<void>;
}

@Injectable()
export class AuthApiService extends BaseApiService implements IAuthApiService {
  // implementation
}
```

- **Mock implementations**: Create mock services in `*.mock.ts` files next to the real service:

```ts
// src/shared/api/auth-api.service.mock.ts
@Injectable()
export class AuthApiServiceMock implements IAuthApiService {
  private readonly mockUserData = {
    id: '1',
    email: 'mock@example.com',
    // ...
  };

  async login(model: FormGroup): Promise<UserModel> {
    console.log('[MOCK] AuthApiService.login called with:', model.value);
    await new Promise(resolve => setTimeout(resolve, 500)); // Simulate delay
    return this.createMockUser(model.value.userName);
  }
  
  private createMockUser(userName?: string): UserModel {
    // Create and return mock user
  }
}
```

- **Component usage**: Components inject services normally via constructor. The global configuration handles which implementation is used:

```ts
// ✅ CORRECT: No local providers, uses global config
@Component({
  selector: 'login-auth',
  templateUrl: './login.component.html',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule]
  // NO providers array here!
})
export class LoginComponent {
  constructor(private service: AuthApiService) {}
}
```

```ts
// ❌ WRONG: Local provider overrides global config
@Component({
  selector: 'login-auth',
  templateUrl: './login.component.html',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  providers: [AuthApiService]  // ← NEVER DO THIS!
})
export class LoginComponent {
  constructor(private service: AuthApiService) {}
}
```

- **Mock services for external dependencies**: Create mock implementations for services that depend on external resources (WebSockets, SignalR, etc.):

```ts
// src/shared/chat/chat.service.ts
export interface IChatService {
  msgs: Array<ChatModel>;
  connection?: HubConnection;
  onlineUsers: Array<string>;
  initConnection(): void;
  connectionWebSocket(): Promise<void>;
  send(text: string): Promise<void>;
}

@Injectable()
export class ChatService implements IChatService {
  // Real SignalR implementation
}
```

```ts
// src/shared/chat/chat.service.mock.ts
@Injectable()
export class ChatServiceMock implements IChatService {
  public msgs: Array<ChatModel> = [];
  public connection?: HubConnection = undefined;
  public onlineUsers: Array<string> = [];

  initConnection(): void {
    console.log('[MOCK] ChatService.initConnection - no real connection');
  }

  async connectionWebSocket(): Promise<void> {
    console.log('[MOCK] ChatService.connectionWebSocket - simulating connection');
    await new Promise(resolve => setTimeout(resolve, 100));
  }

  async send(text: string): Promise<void> {
    console.log('[MOCK] ChatService.send:', text);
  }
}
```

```ts
// src/app/app.providers.ts
export const serviceCollection = [
  { provide: AuthApiService, useClass: environment.useMockApi ? AuthApiServiceMock : AuthApiService },
  { provide: ChatService, useClass: environment.useMockApi ? ChatServiceMock : ChatService },
  // ...
];
```

## Testing

We use Angular 20 with Karma + Jasmine for unit tests and run them in a zoneless configuration.

### Stack
- Angular 20 test builder: `@angular/build:karma` (see `angular.json` -> `projects.web.architect.test`).
- Test framework: Jasmine + Karma.
- Browser: ChromeHeadless.
- CSS in tests: Bootstrap 5 + app styles are loaded for the test target (see `angular.json` test options `styles`).
- Zoneless mode: no Zone.js; provide `provideZonelessChangeDetection()` in spec files.

### Zoneless configuration (important)
- Do NOT import Zone.js in tests.
- In each spec that creates components, add `provideZonelessChangeDetection()` to `providers`:

```ts
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, NO_ERRORS_SCHEMA } from '@angular/core';

beforeEach(async () => {
  await TestBed.configureTestingModule({
    imports: [MyComponent],
    providers: [provideZonelessChangeDetection()],
    schemas: [NO_ERRORS_SCHEMA] // optional, for shallow tests
  }).compileComponents();
});
```

- Manually control change detection: call `fixture.detectChanges()` after state updates/events.
- For microtasks (async operations), use `await fixture.whenStable()` before assertions.

### Test naming convention
- Keep test names short and readable: `should <expectation> [when <condition>]`.
- Examples:
  - `should render navbar-toggler on xs`
  - `should show menu items after navbar-toggler click on xs`
  - `should hide navbar-toggler and show right-side buttons on sm+`
  - `should be '/' link on button InGame when success auth`
  - `should be '/set-race' link on button InGame when failed auth`

### Mocks & DI (domain-oriented, no Jasmine spies)
- Unit tests do not use `environment.useMockApi`. Always override providers explicitly in the spec.
- Prefer domain mocks over ad-hoc spies. Example mock with setup method:

```ts
// AuthApiServiceMock example
export class AuthApiServiceMock implements IAuthApiService {
  private hasRace = false;
  SetupAuth(val: boolean) { this.hasRace = val; }
  async VkAuth(): Promise<boolean> { return this.hasRace; }
}
```

- Wire the mock in TestBed and expose it for direct injection via `useExisting`:

```ts
providers: [
  provideRouter([]),
  { provide: AuthApiService, useClass: AuthApiServiceMock },
  { provide: AuthApiServiceMock, useExisting: AuthApiService },
  provideZonelessChangeDetection()
]
```

- Then in tests: `const svc = TestBed.inject(AuthApiServiceMock); svc.SetupAuth(true);`

### Router testing
- Use `provideRouter([...])`.
- If the template uses `<a [routerLink]="...">`, assert the generated `href`:

```ts
const link = el.querySelector('#btn-play') as HTMLAnchorElement;
const href = link.getAttribute('href') ?? link.href;
expect(href.endsWith('/')).toBeTrue(); // or expect(href).toContain('/set-race')
```

- Alternatively, if using `routerLink` on non-anchor elements, assert the bound directive (e.g., query the directive and check its commands).

### Testing responsive markup
- Unit tests do not change the real viewport. Instead, assert Bootstrap utility classes that control visibility:
  - Mobile-only: `d-sm-none`.
  - Hidden on mobile, visible on ≥sm: `d-none d-sm-block`.
  - Collapsible sections: check `collapse` and toggled class `show`.
- Prefer stable test selectors (IDs) on interactive/visible elements to avoid brittle queries.

### Common patterns
- Toggle behavior:

```ts
const toggler = el.querySelector('#navbar-toggler') as HTMLButtonElement;
toggler.click();
fixture.detectChanges();
await fixture.whenStable();
expect(el.querySelector('#collapsed-menu')!.classList.contains('show')).toBeTrue();
```

- Existence/visibility-by-classes:

```ts
const btn = el.querySelector('#btn-rules-sm')!;
expect(btn.classList.contains('d-none')).toBeTrue();
expect(btn.classList.contains('d-sm-block')).toBeTrue();
```

### How to run tests
- All tests (headless, one-off):

```bash
npm run test -- --watch=false --browsers=ChromeHeadless
```

- Single spec file:

```bash
npm run test -- --watch=false --browsers=ChromeHeadless --include=src/app/main/main.component.spec.ts
```

- Watch mode (local dev):

```bash
npm run test
```

- Coverage report (outputs to `coverage/`):

```bash
ng test --code-coverage --browsers=ChromeHeadless --watch=false
```

## Alerts (Global notifications)

- Architecture
  - Service: `src/shared/alerts/alerts.service.ts` (providedIn: 'root'). Uses Angular Signals: `alerts = signal<AlertModel[]>([])` for zoneless + OnPush compatibility.
  - Component: `src/shared/alerts/alerts.component.{ts,html,css}`. `ChangeDetectionStrategy.OnPush`. Renders alerts from the signal.
  - Integration: `AlertsComponent` is standalone; import it where used (add to component `imports` or configure via route-level `imports`).
  - Usage in root template: add a single `<shared-alerts></shared-alerts>` in `src/app/app.html`.

- Service API
  - `push(alertClass, content, timeToCloseMs = 0)`
    - `alertClass`: one of `primary | secondary | success | danger | warning | info | light | dark`.
    - `content`: message text to display.
    - `timeToCloseMs`: auto-close timeout in ms (0 = manual close only).
  - `remove(id: number)`: removes alert by id.

- Component template & styles
  - Template uses the built-in control flow: `@for (alert of alertsService.alerts(); track alert.id)`.
  - Do not use `data-bs-dismiss="alert"`; close via `(click)` which calls service `remove` to keep state consistent.
    - CSS overlay:
    - Fixed container anchored to bottom, full width, high z-index (~1100) to be above navbar/modals.
    - Container has `pointer-events: none`; each `.alert` has `pointer-events: auto` to keep clicks working.
    - `.alert-content { white-space: pre-line; }` to preserve new lines for multi-line messages (e.g., ModelState lines).

## Использование стандартного Tooltip на проекте

- **Важно**
  - Директива `[appTooltip]` и компонент `<app-tooltip />` — standalone. Импортируйте их там, где используются (в `imports` соответствующего компонента или в корневом `AppComponent`). Ничего дополнительно подключать в NgModule не требуется.

- **Что это**
  - Стандартный Tooltip проекта: единый глобальный оверлей (`<app-tooltip />`) + директива `[appTooltip]` для любого хост-элемента.

- **Архитектура**
  - Сервис: `src/shared/tooltip/tooltip.service.ts` (`providedIn: 'root'`), отдает `state$` (observable) — совместимо с zoneless + OnPush.
  - Компонент: `src/shared/tooltip/tooltip.component.{ts,html,css}`, `ChangeDetectionStrategy.OnPush`, рендерит абсолютный оверлей.
  - Директива: `src/shared/tooltip/tooltip.directive.ts`, селектор `[appTooltip]`, `@Input() tooltipPos: 'n'|'s'|'e'|'w'|'nw'` (по умолчанию `'s'`). Наведение покажет, уход — скроет.
  - Глобальное подключение: `<app-tooltip />` добавлен один раз в `src/app/app.html`.

- **Использование**
  - Простой текст:

```html
<button [appTooltip]="'Сохранить изменения'" tooltipPos="e">Сохранить</button>
```

  - HTML-строка:

```html
<table class="topPanel__counters" [appTooltip]="moneyHTML()" tooltipPos="s">...</table>
```

```ts
// component.ts
moneyHTML(): string {
  return `<div>
    Кредиты: ${this.money.Cr}<br/>
    Минералы: ${this.money.Minerals}<br/>
    Газ: ${this.money.Gas}
  </div>`;
}
```

  - Безопасность: содержимое рендерится в компоненте через `[innerHTML]` и проходит стандартную очистку Angular. Не передавайте недоверенный HTML. Для доверенного HTML используйте `DomSanitizer` только при необходимости.
  - Не используйте устаревшие `onmouseover`/`TagToTip`/`UnTip`. Всегда применяйте директиву `[appTooltip]`.

- **Позиционирование**
  - `tooltipPos` определяет сторону: `'n'|'s'|'e'|'w'|'nw'` (по умолчанию `'s'`). Внутри компонента выполняется простое смещение.

- **Подключение**
  - Директива и компонент тултипа — standalone. Импортируйте их там, где используются (в `imports` компонента или глобально в `AppComponent`). NgModules в проекте не используются.

- **Тестирование (zoneless + OnPush)**
  - В спеках импортируйте необходимые standalone-артефакты напрямую (директивы/компоненты), при необходимости также `CommonModule` (для общих директив/пайпов). `SharedModule` не используется:

```ts
await TestBed.configureTestingModule({
  imports: [CommonModule /* + required standalone directives/components */],
  providers: [provideZonelessChangeDetection()]
}).compileComponents();
```
  - Для проверки поведения директивы эмулируйте `mouseenter`/`mouseleave` на хост-элементе и/или валидируйте состояние `TooltipService`.
