// @ts-check
/**
 * Коды отказов сервера и словари клиента.
 *
 * Сервер отвечает на отказ телом `{ error, statusCode, code?, params? }`, и клиент
 * переводит `code` по словарю (`server-error.util.ts`), а когда перевода нет — показывает
 * русский `error`. Кода, которого нет ни в одном словаре, проверка словарей между собой
 * не видит: все языки согласованно молчат, а человек получает русский текст сервера
 * в английском интерфейсе.
 *
 * Здесь вторая сторона: коды собираются **со стороны сервера** и каждый обязан
 * найтись в эталонном `i18n/en/<scope>.json`, где `scope` — первый сегмент кода.
 *
 * Откуда берутся коды:
 *   1. `new ErrorMessage("код", "текст")` / `ErrorMessage X = new("код", …)` — где угодно
 *      в `src/PetTemplate.{Api,Services,Shared}/**\/*.cs`. Не только в `Constants/`: отказ может быть
 *      объявлен и прямо в сервисе;
 *   2. страховка — любой строковый литерал в `*.cs`, похожий на код и начинающийся
 *      с имени существующего словаря. Ловит объявление мимо первого способа.
 *
 * Второй уровень — словарь с кодами обязан **грузиться хотя бы одним маршрутом**
 * (`withScope` в `app.routes.ts`, `loadScope` в `*.routes.ts`). Связь «эндпоинт ↔ экран»
 * автоматически не выводится, а «словарь ↔ маршрут» — выводится: словарь, который
 * не грузит никто, не переведёт код ни на одном экране.
 *
 * Запуск: `npm run i18n:codes`. Рядом с `npm run i18n:check` — и в воротах
 * `web-tests.yml`, и в `.githooks/pre-commit` (спец. infrastructure, 4 и 5).
 */

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const REPO = join(ROOT, '..', '..', '..');
/** Проекты сервера, в которых объявляются отказы */
const SERVER_DIRS = ['Api', 'Services', 'Shared'].map(dir => join(REPO, 'src', `PetTemplate.${dir}`));
const DICT_DIR = join(ROOT, 'i18n');
const APP_DIR = join(ROOT, 'src', 'app');

/** Язык-эталон: на нём формулировки пишутся первыми */
const REFERENCE_LANG = 'en';

/** Словарь из initial-бандла — он есть на любом экране */
const CORE_SCOPE = 'core';

main();

function main() {
  const dicts = readDictionaries();
  const codes = collectServerCodes(new Set(dicts.keys()));
  const loaded = collectLoadedScopes();

  if (process.argv.includes('--list')) {
    for (const [code, places] of [...codes].sort()) console.log(`${code}\t${places.join(', ')}`);
  }

  checkCodesTranslated(codes, dicts);
  checkScopesReachable(codes, loaded);

  console.log(`i18n-codes: ${codes.size} кодов сервера, все есть в i18n/${REFERENCE_LANG}`);
}

// ── Словари ───────────────────────────────────────────────────────────────────

/** Имя словаря → множество ключей эталонного языка */
function readDictionaries() {
  const dir = join(DICT_DIR, REFERENCE_LANG);
  /** @type {Map<string, Set<string>>} */
  const dicts = new Map();

  for (const file of readdirSync(dir).sort()) {
    if (!file.endsWith('.json')) continue;

    const scope = file.slice(0, -'.json'.length);
    const keys = new Set();

    flatten(JSON.parse(readFileSync(join(dir, file), 'utf8')), scope, keys);
    dicts.set(scope, keys);
  }

  if (!dicts.has(CORE_SCOPE)) fail(`нет словаря \`${CORE_SCOPE}\``);

  return dicts;
}

/**
 * Вложенный JSON → плоские ключи. Лист — строка либо объект плюрализации
 * (есть ключ `other`, все значения — строки)
 *
 * @param {unknown} node @param {string} path @param {Set<string>} keys
 */
function flatten(node, path, keys) {
  if (typeof node === 'string' || node === null || typeof node !== 'object') {
    keys.add(path);
    return;
  }

  const entries = Object.entries(/** @type {Record<string, unknown>} */ (node));
  if (entries.some(([key]) => key === 'other') && entries.every(([, value]) => typeof value === 'string')) {
    keys.add(path);
    return;
  }

  for (const [key, value] of entries) flatten(value, `${path}.${key}`, keys);
}

// ── Коды сервера ──────────────────────────────────────────────────────────────

/**
 * Код → файлы, где он объявлен
 *
 * @param {Set<string>} scopes имена существующих словарей — для страховочного прохода
 * @returns {Map<string, string[]>}
 */
function collectServerCodes(scopes) {
  /** @type {Map<string, string[]>} */
  const codes = new Map();

  const add = (/** @type {string} */ code, /** @type {string} */ file) => {
    const places = codes.get(code) ?? [];
    if (!places.includes(file)) places.push(file);
    codes.set(code, places);
  };

  // Литерал, похожий на код: `scope.block.element` — сегменты из букв и цифр, минимум два
  /** Объявлен ли на сервере сам тип отказа с кодом — без него кодам взяться неоткуда */
  let declared = false;

  const anyCode = new RegExp(`"((?:${[...scopes].join('|')})(?:\\.[A-Za-z0-9]+){1,})"`, 'g');

  for (const dir of SERVER_DIRS) {
    for (const file of walk(dir, name => name.endsWith('.cs'))) {
      const text = readFileSync(file, 'utf8');
      const where = relative(REPO, file);

      if (/\b(?:class|record)\s+ErrorMessage\b/.test(text)) declared = true;

      // `ErrorMessage X = new("код", …)` и `new ErrorMessage("код", …)`
      for (const match of text.matchAll(/ErrorMessage\s+\w+\s*=\s*new\s*\(\s*"([^"]+)"/g)) add(match[1], where);
      for (const match of text.matchAll(/new\s+ErrorMessage\s*\(\s*"([^"]+)"/g)) add(match[1], where);

      for (const match of text.matchAll(anyCode)) add(match[1], where);
    }
  }

  // Пустой список законен, только пока на сервере нет самого `ErrorMessage`: в шаблоне
  // он приходит с отказами аккаунтов (спец. accounts). Объявлен, а кодов ноль — сломан разбор
  if (codes.size === 0 && declared) fail('на сервере не нашлось ни одного кода — сломан разбор');

  return codes;
}

// ── Маршруты ──────────────────────────────────────────────────────────────────

/**
 * Словари, которые грузит хоть один маршрут.
 *
 * Разбор по тексту, не деревом: раздел подключается в `app.routes.ts` через
 * `withScope('scope', import(...), 'ещё', …)`, а лишний словарь экрана — `loadScope('scope')`
 * в `*.routes.ts` раздела. Под каким адресом — не важно: таблицы «словарь → маршруты»
 * в шаблоне нет, нужен только факт загрузки
 *
 * @returns {Set<string>}
 */
function collectLoadedScopes() {
  /** @type {Set<string>} */
  const scopes = new Set();

  for (const file of walk(APP_DIR, name => name.endsWith('.routes.ts'))) {
    const text = readFileSync(file, 'utf8');

    for (const match of text.matchAll(/(?:withScope|loadScope)\(\s*'([^']+)'/g)) scopes.add(match[1]);

    // Лишние словари маршрута — хвостом `withScope('home', import(...), 'assistant')`
    for (const match of text.matchAll(/withScope\(\s*'[^']+'\s*,\s*import\([^)]*\)((?:\s*,\s*'[^']+')+)/g)) {
      for (const extra of match[1].matchAll(/'([^']+)'/g)) scopes.add(extra[1]);
    }
  }

  if (scopes.size === 0) fail('ни один маршрут не грузит словарь — сломан разбор `*.routes.ts`');

  return scopes;
}

// ── Проверки ──────────────────────────────────────────────────────────────────

/**
 * Каждый код сервера есть в эталонном словаре. Что ключ есть и в остальных
 * языках — забота сверки словарей между собой
 *
 * @param {Map<string, string[]>} codes @param {Map<string, Set<string>>} dicts
 */
function checkCodesTranslated(codes, dicts) {
  /** @type {string[]} */
  const missing = [];

  for (const [code, places] of [...codes].sort()) {
    const keys = dicts.get(scopeOf(code));

    if (keys == null) missing.push(`${code} — нет словаря \`${scopeOf(code)}\` (${places.join(', ')})`);
    else if (!keys.has(code)) missing.push(`${code} — нет ключа в i18n/${REFERENCE_LANG} (${places.join(', ')})`);
  }

  if (missing.length > 0) {
    fail(`коды сервера без перевода (${missing.length}):\n  ${missing.join('\n  ')}`);
  }
}

/**
 * Словарь, в котором лежат коды сервера, обязан где-то грузиться: иначе
 * `translateCode` не найдёт ключ ни на одном экране и человек увидит русский
 * текст сервера. `core` в проверку не входит — он в initial-бандле
 *
 * @param {Map<string, string[]>} codes @param {Set<string>} loaded
 */
function checkScopesReachable(codes, loaded) {
  const orphans = [...new Set([...codes.keys()].map(scopeOf))]
    .filter(scope => scope !== CORE_SCOPE && !loaded.has(scope))
    .sort();

  if (orphans.length > 0) {
    fail(
      `словари с кодами сервера, которые не грузит ни один маршрут: ${orphans.join(', ')}.\n` +
      '  Такой код не переведётся нигде — либо `withScope`/`loadScope` в маршрутах, либо перенос кода в `core`'
    );
  }
}

// ── Мелочи ────────────────────────────────────────────────────────────────────

/** Имя словаря — первый сегмент кода @param {string} code */
function scopeOf(code) {
  return code.slice(0, code.indexOf('.'));
}

/**
 * Файлы по дереву. `bin` и `obj` пропускаем: там лежат копии исходников
 * и сгенерированное, и один и тот же код нашёлся бы трижды. `Migrations` —
 * тысячи строк снимков модели, кодов отказов там не бывает
 *
 * @param {string} dir @param {(name: string) => boolean} accept @returns {Generator<string>}
 */
function* walk(dir, accept) {
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj' || entry === 'node_modules' || entry === 'Migrations') continue;

    const path = join(dir, entry);

    if (statSync(path).isDirectory()) yield* walk(path, accept);
    else if (accept(entry)) yield path;
  }
}

/** @param {string} message @returns {never} */
function fail(message) {
  console.error(`i18n-codes: ${message}`);
  process.exit(1);
}
