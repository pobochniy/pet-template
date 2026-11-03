/**
 * Общая среда тестов продукта и админки: Vitest в jsdom (`@angular/build:unit-test`).
 * Подключается `setupFiles` в `angular.json` — у админки напрямую, у продукта через
 * `src/test-setup.ts` — и выполняется перед каждым файлом спек. Глобальные функции
 * тестов есть только в `tsconfig.spec.json`, поэтому из `tsconfig.app.json` файл исключён.
 */

// Шпионы `vi.spyOn` снимаются после каждого теста, как снимал их Jasmine: подмена
// `Storage.prototype.setItem` или `router.navigateByUrl` в одном тесте не достаётся
// следующему. `vi.fn()` это не трогает — их создают заново в `beforeEach`
afterEach(() => vi.restoreAllMocks());

// `localStorage` и `sessionStorage` — из jsdom, на любом Node. Vitest переносит `window`
// jsdom на глобальный объект, но только те имена, которых там ещё нет, а Node 25 сам
// держит оба хранилища на `globalThis` (Web Storage без `--localstorage-file` — объект
// без `getItem`/`setItem`). Подменяем не глядя: само чтение хранилища Node печатает
// предупреждение, а на Node 22 (`web-tests.yml`) там и так хранилище jsdom
const dom = (globalThis as unknown as { jsdom?: { window: Window } }).jsdom?.window;

for (const name of ['localStorage', 'sessionStorage'] as const) {
  if (dom) Object.defineProperty(globalThis, name, { get: () => dom[name], configurable: true });
}

// Прокрутки в jsdom нет вовсе (раскладки нет — некуда прокручивать), а компоненты зовут
// `scrollIntoView` у поля с ошибкой. Пустая заглушка: что прокрутка нужна, тесты не проверяют
Element.prototype.scrollIntoView ??= () => {};
