// @ts-check
/**
 * Самопроверка `i18n-lint.mjs` на образцах: случаи, на которых ошибался разбор
 * регулярками (вложенный шаблон, апостроф, regexp с бэктиком, `/*` в строке), и правила
 * переводимых атрибутов. Запуск: `npm run test:tools`.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { lintTemplate, lintTypeScript } from './i18n-lint.mjs';

const ts = (/** @type {string} */ text) => lintTypeScript('x.ts', text);
const html = (/** @type {string} */ text, allowed = undefined) => lintTemplate('x.html', text, 0, allowed);

test('ts: русская строка в кавычках любого вида', () => {
  assert.deepEqual(ts(`const a = 'Привет';`), ['x.ts:1: русская строка в коде — «const a = \'Привет\';»']);
  assert.equal(ts(`const a = "Привет";`).length, 1);
  assert.equal(ts('const a = `Привет`;').length, 1);
  assert.equal(ts('const a = `${n} участников`;').length, 1);
});

test('ts: две строки на одной строке файла — одна ошибка', () => {
  assert.equal(ts(`const a = ['Да', 'Нет'];`).length, 1);
});

test('ts: английский текст, комментарии и regexp с кириллицей законны', () => {
  assert.deepEqual(ts(`const a = 'Hello'; // Привет`), []);
  assert.deepEqual(ts(`/* 'Привет' */\nconst a = 1;`), []);
  assert.deepEqual(ts(`/**\n * \`Привет\`\n */\nconst a = 1;`), []);
  assert.deepEqual(ts(`const r = /[А-Яа-я]+/;`), []);
});

test('ts: вложенный шаблон в подстановке не сбивает пары', () => {
  // Регулярки спаривали четыре бэктика как два литерала, и русский хвост оказывался «вне строки»
  assert.equal(ts('const a = `x ${`y`} в`;').length, 1);
  assert.deepEqual(ts('const a = `x ${`y`} z`;\nconst b = 1; // в'), []);
  // Кириллица только во вложенном шаблоне — тоже текст
  assert.equal(ts('const a = `x ${f(`в`)} z`;').length, 1);
});

test('ts: апостроф внутри шаблона не открывает строку', () => {
  assert.deepEqual(ts("const a = `it's`;\nconst b = 1; // а'б"), []);
  assert.deepEqual(ts("const a = `it's\n  fine`;\nconst b = 'ok'; // 'Привет'"), []);
});

test('ts: regexp с бэктиком и `/*` в строке не ломают разбор', () => {
  assert.deepEqual(ts('const r = /`/;\nconst a = `\n  ok\n`; // `Привет`'), []);
  assert.deepEqual(ts("const a = '/*';\nconst b = 'Привет';"), ['x.ts:2: русская строка в коде — «const b = \'Привет\';»']);
});

test('ts: многострочный шаблон с кириллицей — со строки начала', () => {
  const errors = ts('const a = 1;\nconst b = `\n  Привет\n`;');
  assert.equal(errors.length, 1);
  assert.match(errors[0], /^x\.ts:2: русская строка в многострочном шаблоне — «`/);
});

test('ts: console и localStorage — по вызову, хоть и на несколько строк', () => {
  assert.deepEqual(ts(`console.warn('Ошибка');`), []);
  assert.deepEqual(ts(`console.error(\n  'Ошибка',\n  \`ещё \${x}\`\n);`), []);
  assert.deepEqual(ts(`localStorage.setItem('ключ', v);`), []);
  assert.equal(ts(`alert('Ошибка');`).length, 1);
});

test('ts: инлайновый template — правилами шаблона, номер строки от начала файла', () => {
  const source = [
    "@Component({",
    "  selector: 'x',",
    "  template: `",
    "    <!-- Комментарий законен -->",
    "    <p>{{ i18n.t('a.b') }}</p>",
    "    <p>Привет</p>",
    "  `,",
    "  styles: [`p { color: red }`]",
    "})",
    "class X {}"
  ].join('\n');
  assert.deepEqual(ts(source), ['x.ts:6: текст в шаблоне — «Привет»']);
});

test('html: текстовый узел и текст вокруг интерполяции', () => {
  assert.deepEqual(html('<p>Save</p>'), ['x.html:1: текст в шаблоне — «Save»']);
  assert.deepEqual(html('<p>\n  Привет, {{ name }}!</p>'), ['x.html:2: текст в шаблоне — «Привет, !»']);
  assert.deepEqual(html('<p>{{ name }}: {{ count }}</p>'), []);
});

test('html: блоки управления, комментарии, script и бренды — не текст', () => {
  const template = [
    '@if (user(); as u) {',
    '  <span>{{ u.name }}</span>',
    '} @else {',
    '  <span>&nbsp;—&nbsp;</span>',
    '}',
    '@for (item of items(); track item.id) { <i>{{ item }}</i> } @empty { <b>{{ none }}</b> }',
    '@let total = items().length;',
    '<!-- Пояснение по-русски -->',
    '<script>window.x = "Привет";</script>',
    '<span>PetTemplate</span>'
  ].join('\n');
  assert.deepEqual(html(template), []);
});

test('html: текст внутри блока ловится с его строкой', () => {
  assert.deepEqual(html('@if (x) {\n  <p>Привет</p>\n}'), ['x.html:2: текст в шаблоне — «Привет»']);
});

test('attrs: литерал в переводимом атрибуте — на любом языке и в любой записи', () => {
  assert.deepEqual(html('<i title="Save"></i>'), ['x.html:1: текст в атрибуте title — «Save»']);
  assert.equal(html('<i title="Сохранить"></i>').length, 1);
  assert.equal(html(`<i [title]="'Save'"></i>`).length, 1);
  assert.equal(html(`<i [attr.aria-label]="'Close'"></i>`).length, 1);
  assert.equal(html(`<input placeholder="Login {{ x }}" />`).length, 1);
  assert.equal(html(`<i [title]="on() ? 'On' : 'Off'"></i>`).length, 2);
});

test('attrs: ключ, выражение и пустое значение законны', () => {
  assert.deepEqual(html(`<i [title]="i18n.t('core.nav.profile')"></i>`), []);
  assert.deepEqual(html(`<i [attr.aria-label]="i18n.t(on() ? 'a.on' : 'a.off')"></i>`), []);
  assert.deepEqual(html('<img alt="" src="x.png" />'), []);
  assert.deepEqual(html(`<i [title]="user().name"></i>`), []);
  // Не переводимый атрибут: имя класса, тип поля
  assert.deepEqual(html('<input type="text" class="form-control" />'), []);
});

test('attrs: техническое значение проходит только строкой исключения', () => {
  const template = `<img [alt]="'avatar'" src="x.png" aria-hidden="true" />`;
  assert.equal(html(template).length, 1);

  const allowed = [{ attr: 'alt', value: 'avatar', why: 'картинка скрыта aria-hidden' }];
  assert.deepEqual(html(template, allowed), []);
  // Исключение — точным значением и своим атрибутом, а не словом вообще
  assert.equal(html(`<img alt="avatar photo" />`, allowed).length, 1);
  assert.equal(html(`<i title="avatar"></i>`, allowed).length, 1);
  // Сужение по файлу
  const elsewhere = [{ ...allowed[0], file: /other\.html$/ }];
  assert.equal(html(template, elsewhere).length, 1);
});

test('html: неразборчивый шаблон — ошибка, а не тишина', () => {
  assert.match(html('@if (x) {\n<p>a</p>')[0] ?? '', /x\.html:1: шаблон не разобрался — Unclosed block "if"/);
});
