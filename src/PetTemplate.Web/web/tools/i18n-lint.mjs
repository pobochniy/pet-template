// @ts-check
/**
 * Защита от нового непереведённого текста (`src/PetTemplate.Web/agents.md`, «Локализация»).
 *
 * Ищет четыре вещи:
 *   1. текстовый узел с буквами в `*.html` и в инлайновых шаблонах;
 *   2. переводимый атрибут (`title`, `aria-label`, `placeholder`, `alt`)
 *      со строковым значением вместо выражения;
 *   3. строковый литерал с кириллицей в `*.ts`;
 *   4. многострочный шаблонный литерал с кириллицей — построчная проверка (3)
 *      его не видит, см. `checkMultilineTemplates`.
 *
 * Правило одно: текста в коде не бывает, бывают ключи. Исключения — ниже,
 * каждое с причиной; всё остальное чинится ключом в `i18n/`.
 *
 * Запуск: `npm run i18n:lint`. Рядом с `npm run i18n:check` — и в воротах
 * `web-tests.yml`, и в `.githooks/pre-commit` (спец. infrastructure, 4 и 5).
 */

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SOURCE = join(ROOT, 'src');

/** Атрибуты, которые видит человек. Значение у них должно быть выражением `[attr]="…"` */
const TRANSLATABLE_ATTRS = ['title', 'aria-label', 'placeholder', 'alt'];

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
 * Строка, попавшая сюда по ошибке, — это непереведённый текст, который тест
 * пропустит: список короткий намеренно.
 */
const ALLOWED_TS = [
  // Ключи `localStorage` и данные, а не подписи
  /localStorage\.(get|set|remove)Item/,
  // Сообщения в консоль: их читает разработчик, а не пользователь
  /console\.(log|warn|error|info)/
];

/**
 * Строковый литерал с кириллицей внутри — обычный или шаблонный.
 *
 * Шаблонный проверяем в границах строки; многострочный — отдельным проходом
 * (`checkMultilineTemplates`), потому что построчно он не виден.
 * Подстановку `${…}` разбирать не нужно: кириллица в ней всё равно внутри
 * тех же обратных кавычек
 */
const TS_LITERAL = /`[^`]*[А-Яа-яЁё][^`]*`|'[^']*[А-Яа-яЁё][^']*'|"[^"]*[А-Яа-яЁё][^"]*"/;

const errors = [];

for (const file of walk(SOURCE)) {
  if (SKIP.some(rule => rule.test(file))) continue;

  const text = readFileSync(file, 'utf8');

  if (file.endsWith('.html')) checkTemplate(file, text);
  else if (file.endsWith('.ts')) checkTypeScript(file, text);
}

if (errors.length > 0) {
  console.error(`i18n-lint: нашёлся текст мимо словаря (${errors.length}):`);
  for (const error of errors) console.error(`  ${error}`);
  process.exit(1);
}

console.log('i18n-lint: текста мимо словаря не нашлось');

/** @param {string} dir @returns {Generator<string>} */
function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);

    if (statSync(path).isDirectory()) yield* walk(path);
    else yield path;
  }
}

/** @param {string} file @param {string} text */
function checkTemplate(file, text) {
  // Комментарии убираем целиком: в них объясняют решения, и по-русски
  // Инлайновый `<script>` — код, а не текст (в `index.html` так снимаются параметры mini-app
  // до старта Angular): вырезается целиком, чтобы не ронять проверку
  const clean = text
    .replace(/<!--[\s\S]*?-->/g, match => match.replace(/[^\n]/g, ' '))
    .replace(/<script\b[\s\S]*?<\/script>/g, match => match.replace(/[^\n]/g, ' '));

  checkTextNodes(file, clean);
  checkAttributes(file, clean);
}

/**
 * Текстовый узел — то, что между тегами. Интерполяция `{{ … }}` текстом
 * не считается: в ней выражение, и перевод там уже есть либо его нет по существу
 * (имя пользователя, название книги).
 *
 * @param {string} file @param {string} text
 */
function checkTextNodes(file, text) {
  const withoutInterpolation = text
    .replace(/\{\{[\s\S]*?\}\}/g, ' ')
    // Блоки управления шаблона (`@if (…) {`, `@else {`, `}`) стоят между тегами,
    // но текстом не являются: это код
    .replace(/@\w+[^{<>]*\{/g, ' ')
    // `@let name = выражение;` — тоже код, но фигурной скобки за ним нет
    .replace(/@let[^;<>]*;/g, ' ')
    .replace(/[{}]/g, ' ');

  // Содержимое между `>` и `<`
  for (const match of withoutInterpolation.matchAll(/>([^<>]+)</g)) {
    const content = match[1];
    if (!hasWords(content)) continue;

    errors.push(`${where(file, withoutInterpolation, match.index)}: текст в шаблоне — «${trim(content)}»`);
  }
}

/**
 * Переводимый атрибут в двух формах:
 *   `attr="текст"`      — обычный атрибут со строкой вместо выражения;
 *   `[attr]="'текст'"`  — привязка, в которой выражение и есть строковый литерал.
 *
 * Вторая форма — документированная запись `sharedTooltip`, и без неё проверка
 * этой директивы была мёртвой: lookbehind `(?<![\[\w-])` по построению
 * пропускает всё, что стоит в квадратных скобках.
 *
 * @param {string} file @param {string} text
 */
function checkAttributes(file, text) {
  for (const attr of TRANSLATABLE_ATTRS) {
    const plain = new RegExp(`(?<![\\[\\w-])${attr}\\s*=\\s*"([^"]*)"`, 'g');
    // `[attr]="'…'"`: литерал внутри привязки. Выражение целиком не трогаем —
    // `[title]="i18n.t('…')"` законно и ключ в нём тоже в одинарных кавычках,
    // поэтому требуем, чтобы литерал занимал всё значение
    const bound = new RegExp(`\\[\\s*${attr}\\s*\\]\\s*=\\s*"\\s*'([^']*)'\\s*"`, 'g');

    for (const pattern of [plain, bound]) {
      for (const match of text.matchAll(pattern)) {
        if (!hasWords(match[1])) continue;

        errors.push(`${where(file, text, match.index)}: текст в атрибуте ${attr} — «${trim(match[1])}»`);
      }
    }
  }
}

/**
 * В `*.ts` ловим только кириллицу: английская строка бывает и кодом, и именем
 * поля, и css-классом, а русская — почти всегда подпись.
 *
 * Инлайновые шаблоны проверяем теми же правилами, что и файлы.
 *
 * @param {string} file @param {string} text
 */
function checkTypeScript(file, text) {
  const inline = text.match(/template:\s*`([\s\S]*?)`/);
  if (inline) checkTemplate(file, inline[1]);

  checkMultilineTemplates(file, text);

  const lines = text.split('\n');
  let inBlockComment = false;

  lines.forEach((line, index) => {
    const code = stripComments(line, inBlockComment);
    inBlockComment = code.inBlockComment;

    if (!/[А-Яа-яЁё]/.test(code.text)) return;
    if (!/['"`]/.test(code.text)) return;
    if (ALLOWED_TS.some(rule => rule.test(code.text))) return;

    // Строковый литерал с кириллицей внутри — в том числе шаблонный:
    // `${count} участников` кавычек не имеет, а подписью является ровно так же
    if (!TS_LITERAL.test(code.text)) return;

    errors.push(`${file.replace(ROOT + '/', '')}:${index + 1}: русская строка в коде — «${trim(code.text)}»`);
  });
}

/**
 * Многострочный шаблонный литерал с кириллицей.
 *
 * Отдельным проходом, потому что построчная проверка его не видит: на строке
 * с русским текстом внутри такого литерала нет ни одной кавычки, и
 * `checkTypeScript` выходит раньше проверки литерала.
 *
 * Лексера тут нет и не нужно — хватает спаривания обратных кавычек по всему
 * файлу, если сначала убрать те, что кавычками не являются: комментарии,
 * содержимое обычных строк и regexp-литералы с бэктиком внутри (` ``` ` в
 * разметке markdown — ровно такой случай). Чем этот способ ошибается —
 * в `tech-debt/i18n-lint-lexer.md`.
 *
 * Инлайновые `template:` и `styles:` пропускаем: шаблон разбирается правилами
 * html (`checkTemplate`), где русский комментарий законен, а в `styles:`
 * кириллице взяться неоткуда
 *
 * @param {string} file @param {string} text
 */
function checkMultilineTemplates(file, text) {
  const code = blankComments(text);
  const scannable = blankQuoted(code);

  // Пары бэктиков по порядку: содержимое литерала уже очищено от чужих кавычек,
  // поэтому первая закрывающая — та самая
  for (const match of scannable.matchAll(/`(?:\\.|[^`\\])*`/g)) {
    if (!match[0].includes('\n')) continue;

    // Кириллицу ищем в исходном тексте: `blankQuoted` мог затереть её внутри
    // литерала, а границы литерала от этого не поехали — затирание сохраняет длину
    const literal = code.slice(match.index, match.index + match[0].length);
    if (!/[А-Яа-яЁё]/.test(literal)) continue;

    const before = code.slice(0, match.index);
    if (/(template|styles)\s*:\s*$/.test(before)) continue;
    if (ALLOWED_TS.some(rule => rule.test(lastLine(before)))) continue;

    errors.push(
      `${where(file, code, match.index)}: русская строка в многострочном шаблоне — «${trim(literal)}»`
    );
  }
}

/**
 * Комментарии — пробелами вместо вырезания: длина и переносы строк сохраняются,
 * поэтому позиция совпадает с исходным файлом и номер строки не врёт.
 *
 * `//` режем с начала слова, как и `stripComments`: внутри адреса
 * (`https://…`) двойной слэш комментарием не является
 *
 * @param {string} text
 */
function blankComments(text) {
  return text
    .replace(/\/\*[\s\S]*?\*\//g, blank)
    .split('\n')
    .map(line => line.replace(/(^|\s)\/\/.*$/, (match, space) => space + blank(match.slice(space.length))))
    .join('\n');
}

/**
 * Содержимое обычных строк и regexp-литералов с бэктиком — пробелами.
 *
 * Обе конструкции умеют нести бэктик, который литерала не открывает, и без
 * этого спаривание разъезжается на первом же `` /```/ ``. Regexp затираем
 * только с бэктиком внутри: наивный разбор путает его с делением, и пусть уж
 * ошибка достаётся редкой строке, а не каждой формуле
 *
 * @param {string} text
 */
function blankQuoted(text) {
  return text
    .replace(/'(?:\\.|[^'\\\n])*'|"(?:\\.|[^"\\\n])*"/g, blank)
    .replace(/\/(?:\\.|\[(?:\\.|[^\]\\\n])*\]|[^/\\\n])+\/[a-z]*/g, match =>
      match.includes('`') ? blank(match) : match
    );
}

/** Замена куска пробелами с сохранением переносов — позиции остальных символов не меняются */
function blank(value) {
  return value.replace(/[^\n]/g, ' ');
}

function lastLine(text) {
  return text.slice(text.lastIndexOf('\n') + 1);
}

/**
 * Строка без комментариев. Возвращает и состояние блочного комментария:
 * он тянется через строки, и без него закомментированный русский текст
 * считался бы кодом
 *
 * @param {string} line @param {boolean} inBlockComment
 */
function stripComments(line, inBlockComment) {
  let text = line;

  if (inBlockComment) {
    const end = text.indexOf('*/');
    if (end < 0) return { text: '', inBlockComment: true };

    text = text.slice(end + 2);
    inBlockComment = false;
  }

  text = text.replace(/\/\*[\s\S]*?\*\//g, ' ');

  const start = text.indexOf('/*');
  if (start >= 0) {
    text = text.slice(0, start);
    inBlockComment = true;
  }

  // `//` внутри строки — редкость, а внутри адреса — нет: режем только с начала слова
  text = text.replace(/(^|\s)\/\/.*$/, '$1');

  return { text, inBlockComment };
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

function where(file, text, index) {
  return `${relative(ROOT, file)}:${text.slice(0, index).split('\n').length}`;
}
