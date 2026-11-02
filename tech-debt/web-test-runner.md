# Тесты клиента — на Karma и Jasmine

Клиент на Angular 21, а тесты (`ng test pet-template`, `ng test admin`; ворота
`web-tests.yml`) идут на Karma с Jasmine. Karma объявлена устаревшей, с Angular 21 новые
проекты по умолчанию получают Vitest (`@angular/build:unit-test`). Отстают и сами пакеты:
`jasmine-core` 5.9 и `@types/jasmine` 5.1 при вышедших 7.x, TypeScript 6.0 при вышедшем 7.0
(Angular 21 на 7.0 не рассчитан — переходить вместе со следующим Angular).

Пока работает: прогон зелёный (346 + 122), Chrome на облачном раннере есть.

## Что сделать

- Переезд на Vitest: `ng g @schematics/angular:refactor-jasmine-vitest` по обоим проектам,
  builder `unit-test` в `angular.json`, затем руками — `jasmine.createSpyObj`, `fakeAsync`/`tick`
  и шпионы на `HttpTestingController`, которые схема не переписывает.
- Режим — браузерный (Playwright/Chromium) или jsdom: jsdom быстрее и не требует Chrome в
  `web-tests.yml`, но компоненты с раскладкой и `IntersectionObserver` проверять в нём нельзя.
- Убрать `karma*`, `jasmine*`; обновить `src/PetTemplate.Web/agents.md`
  (команды тестов), `README.md` и `specs/infrastructure.md` (ворота `web-tests.yml`).

Делать отдельной задачей, не попутно: правка затрагивает все спеки клиента.
