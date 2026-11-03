# Bootstrap целиком или поштучно через `@import`

Шаблон собирает Bootstrap поштучно — `src/PetTemplate.Web/web/src/bootstrap.scss`
(стили 230.98 → 110.18 КБ). Бюджет прижал: с `bootstrap.min.css` initial был 518 КБ, а ошибка
бюджета — 410 (`bundle.md`). Цена — `@import`, который уезжает в Dart Sass 3.0, и
`silenceDeprecations: ["import"]` в `angular.json` у `build` и `test`. Это выбор между двумя
долгами; ниже — цена каждого и путь назад.

## Цена каждого варианта

| Сборка | minified | gzip |
|---|---|---|
| поштучный `@import` | 123 КБ | 19 КБ |
| весь Bootstrap (исходный шаблон; то же даёт `@use "bootstrap/scss/bootstrap"`) | 230 КБ | 31 КБ |

## Поштучная сборка: что пришло вместе с ней

`src/bootstrap.scss` собирает Bootstrap 5.3 двумя десятками `@import`. Dart Sass ругается
на каждый (`DEPRECATION WARNING [import]`), поэтому в `angular.json` у `build` и `test`
стоит:

```json
"stylePreprocessorOptions": {
  "includePaths": ["node_modules"],
  "sass": { "silenceDeprecations": ["import"] }
}
```

`@import` уезжает в Dart Sass 3.0. Пока флаг молчит — сборка идёт, но однажды встанет.

## Почему нельзя просто переписать на `@use`

Bootstrap 5.3 не модульный. Его партиалы не подключают свои зависимости —
`_buttons.scss` начинается сразу с `.btn { ... $btn-padding-x ... }` и рассчитывает,
что переменные и миксины уже лежат в глобальной области, куда их занёс `@import`
в `bootstrap.scss`. `@use` даёт каждому файлу собственную область, поэтому
поштучная сборка через `@use` не компилируется вовсе:

```scss
@use "bootstrap/scss/functions";
@use "bootstrap/scss/variables";   // Error: Undefined mixin _assert-ascending
@use "bootstrap/scss/buttons";
```

Внутри самого `_variables.scss` тоже есть `@import "variables-dark"` — то есть
переезд на `@use` невозможен не только у нас, но и в апстриме. Модули обещаны
в Bootstrap 6; до него `@import` — единственный рабочий способ. `@use` компилируется
ровно в одном варианте — целиком (`@use "bootstrap/scss/bootstrap" with (...)`), и это
та же цена, что у `bootstrap.min.css`; к тому же `bootstrap.scss` из пакета сам состоит
из `@import`, так что Dart Sass 3.0 сломает и его.

## Насколько широко бьёт флаг

Узко, если `bootstrap.scss` — единственный Sass-файл, а компоненты объявляют `styleUrls`
с `.css`. Предупреждения из `node_modules` Angular и так глушит сам (`quietDeps: true`
в `@angular/build`), так что флаг закрывает ровно те `@import`, что написаны в этом файле.

## Когда встанет сборка

Не внезапно. `@angular/build` тянет `sass` точной версией, без каретки, так что
Dart Sass 3.0 приедет только вместе с мажором Angular — через `package.json`, а не сам
по себе. Ломается всё в один момент: и наш файл, и партиалы Bootstrap.

## Проверено

2026-11-03, Bootstrap 5.3.8: партиалы по-прежнему без собственных `@use` (`_buttons.scss`
начинается сразу с правил), `_variables.scss` всё так же `@import`-ит `variables-dark`.
Поштучной сборки на `@use` нет и в этой версии — переезжать не на что.

## План

На Bootstrap 6 не переходим (решение владельца 2026-10-29) — остаёмся на 5.3.

1. ~~Пока бюджет позволяет — `bootstrap.min.css` целиком, флага нет.~~
2. Бюджет прижал — поштучный `src/bootstrap.scss` с флагом `silenceDeprecations`. **Сейчас здесь.**
3. Мажор Angular потянет Sass 3.0 — сначала `overrides` в `package.json` на последний
   `sass@1`; не проходит — назад к `bootstrap.min.css`, свои цвета — переменными `--bs-*`,
   бюджет пересмотреть, флаг из `angular.json` убрать.
