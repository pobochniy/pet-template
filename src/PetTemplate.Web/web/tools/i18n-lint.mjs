// @ts-check
/**
 * Защита от нового непереведённого текста (`src/PetTemplate.Web/agents.md`, «Локализация»).
 *
 * Ищет три вещи:
 *   1. текстовый узел с буквами в `*.html` и в инлайновых шаблонах (`template:`);
 *   2. переводимый атрибут (`title`, `aria-label`, `placeholder`, `alt`) с текстом
 *      вместо ключа: `title="…"`, `[title]="'…'"`, `[attr.aria-label]="'…'"`,
 *      `title="… {{ x }}"`;
 *   3. строковый литерал с кириллицей в `*.ts` — обычный и шаблонный, однострочный
 *      и многострочный.
 *
 * Код разбирают сами компиляторы, а не регулярки: `*.ts` — `typescript`
 * (`ts.createSourceFile`, обход литералов), шаблоны — `parseTemplate` из `@angular/compiler`
 * (тот же разбор, что у сборки). Поэтому вложенный шаблон в подстановке, апостроф внутри
 * литерала, regexp с бэктиком и `/*` внутри строки проверку не сбивают. Скрипту нужны
 * зависимости: в `web-tests.yml` он идёт после `npm ci`.
 *
 * Правило одно: текста в коде не бывает, бывают ключи. Исключения — ниже,
 * каждое с причиной; всё остальное чинится ключом в `i18n/`.
 *
 * Запуск: `npm run i18n:lint`; самопроверка на образцах — `npm run test:tools`
 * (`tools/i18n-lint.test.mjs`). Рядом с `npm run i18n:check` — и в воротах
 * `web-tests.yml`, и в `.githooks/pre-commit` (спец. infrastructure, 4 и 5).
 */

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import ts from 'typescript';
import {
  parseTemplate,
  tmplAstVisitAll,
  TmplAstRecursiveVisitor,
  ASTWithSource,
  Binary,
  Conditional,
  Interpolation,
  LiteralPrimitive,
  ParenthesizedExpression
} from '@angular/compiler';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SOURCE = join(ROOT, 'src');

/** Атрибуты, которые видит человек. Значение у них должно быть выражением `[attr]="…"` */
export const TRANSLATABLE_ATTRS = ['title', 'aria-label', 'placeholder', 'alt'];

/**
 * Значения переводимых атрибутов, которые человеку не показывают, — с причиной у каждой строки.
 *
 * Языки в атрибутах намеренно не различаются: литерал в переводимом атрибуте не нужен ни на
 * каком языке, и `title="Save"` отбивается так же, как `title="Сохранить"`. Если значение
 * всё же не подпись (технический `alt` картинки, которую скрывает `aria-hidden`, имя для
 * автотеста), оно заводится здесь, а не ключом на слово, которого никто не увидит.
 * Каждая строка — точное значение (`value`), а не шаблон слов: выражение с `\w+`
 * вернуло бы щель «английский текст пропустили». `file` — необязательное сужение
 * до файлов (по пути от `web/`).
 *
 * Строка, попавшая сюда по ошибке, — это непереведённый текст, который тест
 * пропустит: список короткий намеренно.
 *
 * @type {{ attr: string, value: string, file?: RegExp, why: string }[]}
 */
export const ALLOWED_ATTRS = [];

/**
 * Файлы, которым переводить нечего.
 *
 * `*.spec.ts` — тесты проверяют подписи и обязаны называть их текстом;
 * `i18n/` — сам движок, его словари и список языков;
 */
const SKIP = [
  /\.spec\.ts$/,
  // `*.mock.ts` — данные для спек: сами спеки уже пропущены правилом выше,
  // а моки лежат отдельными файлами (`shared/api/*.mock.ts`) и под него не попадают.
  // Живыми данными они притворяются намеренно: «Магазин» в них — не подпись
  // интерфейса, а содержимое заметки
  /\.mock\.ts$/,
  /[\\/]i18n[\\/]/,
  /[\\/]generated[\\/]/,
  /[\\/]environments[\\/]/
];

/**
 * Слова, которые не переводятся: имена продуктов и сервисов. Проверка ищет буквы,
 * а бренд из букв и состоит
 */
const BRANDS = ['PetTemplate', 'Telegram', 'ID:'];

/**
 * Разрешённые строки с кириллицей в `*.ts` — с причиной у каждой.
 *
 * Сверяется с вызовом, внутри которого стоит литерал (`console.warn(…)` целиком,
 * хоть и на несколько строк), — не со строкой файла.
 *
 * Строка, попавшая сюда по ошибке, — это непереведённый текст, который тест
 * пропустит: список короткий намеренно.
 */
const ALLOWED_TS = [
  // Ключи `localStorage` и данные, а не подписи
  /localStorage\.(get|set|remove)Item$/,
  // Сообщения в консоль: их читает разработчик, а не пользователь
  /console\.(log|warn|error|info)$/
];

/**
 * Свойства объекта, чей литерал — не текст кода: `template:` проверяется правилами
 * шаблона (`lintTemplate`), где русский комментарий законен, а в `styles:` кириллице
 * взяться неоткуда
 */
const NOT_CODE_PROPERTIES = ['template', 'styles'];

const CYRILLIC = /[А-Яа-яЁё]/;

/* Запуск из командной строки; при импорте (самопроверка) — только функции */
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const errors = [];

  for (const file of walk(SOURCE)) {
    if (SKIP.some(rule => rule.test(file))) continue;

    const name = relative(ROOT, file);
    const text = readFileSync(file, 'utf8');

    if (file.endsWith('.html')) errors.push(...lintTemplate(name, text));
    else if (file.endsWith('.ts')) errors.push(...lintTypeScript(name, text));
  }

  if (errors.length > 0) {
    console.error(`i18n-lint: нашёлся текст мимо словаря (${errors.length}):`);
    for (const error of errors) console.error(`  ${error}`);
    process.exit(1);
  }

  console.log('i18n-lint: текста мимо словаря не нашлось');
}

/** @param {string} dir @returns {Generator<string>} */
function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);

    if (statSync(path).isDirectory()) yield* walk(path);
    else yield path;
  }
}

/**
 * Шаблон Angular: файл `*.html` или инлайновый `template:`.
 *
 * Комментарии и `<script>` (в `index.html` так снимаются параметры mini-app до старта
 * Angular) разбор отбрасывает сам. Интерполяция `{{ … }}` текстом не считается: в ней
 * выражение, и перевод там уже есть либо его нет по существу (имя пользователя,
 * название книги); блоки `@if`, `@for`, `@let` — код, а не текст.
 *
 * @param {string} name путь для сообщения
 * @param {string} text
 * @param {number} [lineOffset] строк до начала шаблона — у инлайнового шаблона номер
 *   строки в сообщении считается от начала `*.ts`
 * @param {typeof ALLOWED_ATTRS} [allowedAttrs]
 * @returns {string[]}
 */
export function lintTemplate(name, text, lineOffset = 0, allowedAttrs = ALLOWED_ATTRS) {
  const errors = [];
  const at = (/** @type {{ sourceSpan: { start: { line: number } } }} */ node) =>
    `${name}:${lineOffset + node.sourceSpan.start.line + 1}`;

  const parsed = parseTemplate(text, name, { preserveWhitespaces: true });
  for (const error of parsed.errors ?? []) {
    errors.push(`${name}:${lineOffset + error.span.start.line + 1}: шаблон не разобрался — ${error.msg}`);
  }

  const allowed = (/** @type {string} */ attr, /** @type {string} */ value) =>
    allowedAttrs.some(rule => rule.attr === attr && rule.value === value && (!rule.file || rule.file.test(name)));

  const report = (node, attr, value) => {
    if (!hasWords(value) || allowed(attr, value)) return;
    errors.push(`${at(node)}: текст в атрибуте ${attr} — «${trim(value)}»`);
  };

  class Visitor extends TmplAstRecursiveVisitor {
    visitText(node) {
      if (hasWords(node.value)) errors.push(`${at(node)}: текст в шаблоне — «${trim(node.value)}»`);
    }

    visitBoundText(node) {
      // Между интерполяциями: `Привет, {{ name }}!` — текст «Привет,» и «!»
      const strings = staticStrings(node.value).join(' ');
      if (hasWords(strings)) errors.push(`${at(node)}: текст в шаблоне — «${trim(strings)}»`);
    }

    visitTextAttribute(node) {
      if (TRANSLATABLE_ATTRS.includes(node.name)) report(node, node.name, node.value);
    }

    visitBoundAttribute(node) {
      // `[title]` — свойство, `[attr.aria-label]` — атрибут: имя у обоих без префикса.
      // Сам литерал (`[title]="'…'"`, ветви `cond ? '…' : '…'`) и текст вокруг
      // интерполяции (`title="… {{ x }}"`); аргумент вызова — `i18n.t('ключ')` — законен
      if (node.type !== 0 && node.type !== 1) return;
      if (!TRANSLATABLE_ATTRS.includes(node.name)) return;

      for (const value of exposedStrings(node.value)) report(node, node.name, value);
    }
  }

  tmplAstVisitAll(new Visitor(), parsed.nodes);
  return errors;
}

/**
 * Статические куски интерполяции (`{{ }}`) — текст между выражениями
 *
 * @param {any} ast
 * @returns {string[]}
 */
function staticStrings(ast) {
  const inner = ast instanceof ASTWithSource ? ast.ast : ast;
  return inner instanceof Interpolation ? inner.strings : [];
}

/**
 * Строки выражения, которые попадут в атрибут как есть: литерал целиком, ветви условия,
 * части сложения и текст вокруг интерполяции. Аргументы вызовов не берём: там ключ
 *
 * @param {any} ast
 * @returns {string[]}
 */
function exposedStrings(ast) {
  if (ast instanceof ASTWithSource) return exposedStrings(ast.ast);
  if (ast instanceof LiteralPrimitive) return typeof ast.value === 'string' ? [ast.value] : [];
  if (ast instanceof ParenthesizedExpression) return exposedStrings(ast.expression);
  if (ast instanceof Conditional) return [...exposedStrings(ast.trueExp), ...exposedStrings(ast.falseExp)];
  if (ast instanceof Binary && ast.operation === '+') return [...exposedStrings(ast.left), ...exposedStrings(ast.right)];
  if (ast instanceof Interpolation) return [ast.strings.join(' '), ...ast.expressions.flatMap(exposedStrings)];
  return [];
}

/**
 * `*.ts`: строковые литералы с кириллицей и инлайновые шаблоны.
 *
 * Ловим только кириллицу: английская строка бывает и кодом, и именем поля, и css-классом,
 * а русская — почти всегда подпись. Подстановка `${…}` шаблонного литерала — код:
 * литералы в ней проверяются сами по себе, а кириллица в статических кусках —
 * у самого шаблона (`${count} участников` — подпись ровно так же).
 *
 * Однострочный литерал — одной ошибкой на строку («русская строка в коде», с текстом
 * строки); литерал на несколько строк — «в многострочном шаблоне», с текстом литерала.
 *
 * @param {string} name путь для сообщения
 * @param {string} text
 * @returns {string[]}
 */
export function lintTypeScript(name, text) {
  const errors = [];
  const reportedLines = new Set();
  const source = ts.createSourceFile(name, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
  const lines = text.split('\n');
  const lineOf = (/** @type {number} */ pos) => source.getLineAndCharacterOfPosition(pos).line;

  /** @param {ts.Node} node */
  const visit = node => {
    const property = notCodeProperty(node);
    if (property) {
      if (property === 'template') lintInlineTemplate(node);
      return;
    }

    if (isTextLiteral(node)) {
      checkLiteral(node);
      // У шаблона с подстановками дети — выражения `${…}`: в них свои литералы
      if (!ts.isTemplateExpression(node)) return;
    }

    ts.forEachChild(node, visit);
  };

  /** @param {ts.StringLiteral | ts.NoSubstitutionTemplateLiteral | ts.TemplateExpression} node */
  const checkLiteral = node => {
    const content = ts.isTemplateExpression(node)
      ? [node.head.text, ...node.templateSpans.map(span => span.literal.text)].join('')
      : node.text;

    if (!CYRILLIC.test(content)) return;
    if (isAllowedCall(node)) return;

    const start = lineOf(node.getStart(source));
    const end = lineOf(node.getEnd());

    if (start !== end) {
      errors.push(`${name}:${start + 1}: русская строка в многострочном шаблоне — «${trim(node.getText(source))}»`);
      return;
    }

    if (reportedLines.has(start)) return;
    reportedLines.add(start);
    errors.push(`${name}:${start + 1}: русская строка в коде — «${trim(lines[start])}»`);
  };

  /** Инлайновый `template:` — правилами шаблона, с номерами строк от начала файла */
  const lintInlineTemplate = node => {
    if (!ts.isStringLiteral(node) && !ts.isNoSubstitutionTemplateLiteral(node)) return;
    // Позиция — сразу за открывающей кавычкой: строки шаблона считаются от неё
    errors.push(...lintTemplate(name, node.text, lineOf(node.getStart(source))));
  };

  ts.forEachChild(source, visit);
  return errors;
}

/**
 * @param {ts.Node} node
 * @returns {node is ts.StringLiteral | ts.NoSubstitutionTemplateLiteral | ts.TemplateExpression}
 */
function isTextLiteral(node) {
  return ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) || ts.isTemplateExpression(node);
}

/**
 * Значение свойства `template:`/`styles:` (литерал или массив литералов) — имя свойства,
 * иначе `undefined`
 *
 * @param {ts.Node} node
 */
function notCodeProperty(node) {
  let owner = node.parent;
  if (owner && ts.isArrayLiteralExpression(owner)) owner = owner.parent;
  if (!owner || !ts.isPropertyAssignment(owner) || owner.name === node) return undefined;
  if (owner.initializer !== node && owner.initializer !== node.parent) return undefined;

  const key = ts.isIdentifier(owner.name) || ts.isStringLiteral(owner.name) ? owner.name.text : '';
  return NOT_CODE_PROPERTIES.includes(key) ? key : undefined;
}

/** Литерал внутри разрешённого вызова (`console.warn(…)`) — по вызову, а не по строке файла */
function isAllowedCall(/** @type {ts.Node} */ node) {
  for (let current = node.parent; current; current = current.parent) {
    if (ts.isCallExpression(current)) {
      const callee = current.expression.getText();
      if (ALLOWED_TS.some(rule => rule.test(callee))) return true;
    }
  }
  return false;
}

/** Есть ли в куске буквы, а не только знаки препинания и разметка */
function hasWords(value) {
  let text = value.replace(/&[a-z]+;/g, ' ');
  for (const brand of BRANDS) text = text.split(brand).join(' ');

  return /[A-Za-zА-Яа-яЁё]{2,}/.test(text);
}

function trim(value) {
  return value.trim().replace(/\s+/g, ' ').slice(0, 60);
}
