// @ts-check
/**
 * Кодоген словарей: `i18n/{lang}/{scope}.json` → TS-модули в бандле.
 *
 * Почему сборка, а не `fetch` из `public/i18n/`: словари правятся в репозитории
 * и едут с деплоем, то есть сеть здесь ничего не даёт, а стоит дорого —
 * `localStorage`-кеш, `If-None-Match`, версия в ключе, дедупликация одновременных
 * запросов, обработка «не скачалось» и лишний RTT до бутстрапа. Сборка даёт то же
 * самое бесплатно: хеш в имени чанка — это и есть инвалидация кеша.
 *
 * Что появляется на выходе (`src/app/shared/i18n/generated/`):
 *   `{lang}.{scope}.ts` — плоский словарь, `export default { … } as const`;
 *   `index.ts`          — `CORE` (оба языка статическим импортом) и `LOADERS`
 *                         (по `() => import()` на каждый экранный словарь);
 *   `i18n-keys.d.ts`    — типы `I18nKey`, `I18nPluralKey`, `Scope`.
 *
 * Генерируем **в `src/`**, а не рядом с JSON: `tsconfig.app.json` собирает
 * `src/**\/*.ts`, и модуль за пределами этой папки в сборку не попал бы.
 *
 * Ключи внутри файла пишутся **без имени словаря** — его добавляет кодоген
 * из имени файла. `core.json` с полем `action.save` даёт ключ `core.action.save`:
 * иначе первый сегмент дублировался бы в каждой строке файла.
 *
 * Запуск: `npm run i18n`. Проверка: `npm run i18n:check` — сверяет, что
 * сгенерированное совпадает с закоммиченным, и тогда типы не отстают от словарей.
 * Стоит воротами в `web-tests.yml` и в `.githooks/pre-commit` (спец. infrastructure, 4 и 5).
 */

import { readdirSync, readFileSync, writeFileSync, mkdirSync, existsSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SOURCE_DIR = join(ROOT, 'i18n');
const OUT_DIR = join(ROOT, 'src', 'app', 'shared', 'i18n', 'generated');

/** Язык-эталон полноты: набор ключей остальных сверяется с ним */
const REFERENCE_LANG = 'en';

/** Словарь, который едет в initial-бандле статическим импортом */
const CORE_SCOPE = 'core';

const HEADER = `/**
 * Сгенерировано \`npm run i18n\` из \`i18n/\`. Руками не правим — правка уедет
 * при следующем запуске кодогена, а CI отобьёт расхождение с исходником.
 */
`;

const checkOnly = process.argv.includes('--check');

main();

function main() {
  const langs = readdirSync(SOURCE_DIR, { withFileTypes: true })
    .filter(entry => entry.isDirectory())
    .map(entry => entry.name)
    .sort();

  if (!langs.includes(REFERENCE_LANG)) {
    fail(`нет папки эталонного языка: i18n/${REFERENCE_LANG}`);
  }

  /** @type {Map<string, Map<string, Record<string, unknown>>>} lang → scope → плоский словарь */
  const dicts = new Map();
  /** @type {Set<string>} */
  const scopes = new Set();

  for (const lang of langs) {
    const byScope = new Map();
    for (const file of readdirSync(join(SOURCE_DIR, lang)).sort()) {
      if (!file.endsWith('.json')) continue;

      const scope = file.slice(0, -'.json'.length);
      const raw = JSON.parse(readFileSync(join(SOURCE_DIR, lang, file), 'utf8'));

      byScope.set(scope, flatten(raw, scope, `i18n/${lang}/${file}`));
      scopes.add(scope);
    }
    dicts.set(lang, byScope);
  }

  if (!scopes.has(CORE_SCOPE)) fail(`нет словаря \`${CORE_SCOPE}\` — он нужен всем экранам`);

  const scopeList = [...scopes].sort();
  const files = new Map();

  for (const lang of langs) {
    for (const scope of scopeList) {
      const dict = dicts.get(lang)?.get(scope);
      if (!dict) fail(`нет файла i18n/${lang}/${scope}.json, а в i18n/${REFERENCE_LANG} он есть`);

      files.set(`${lang}.${scope}.ts`, renderDict(dict));
    }
  }

  files.set('index.ts', renderIndex(langs, scopeList));
  files.set('i18n-keys.d.ts', renderKeys(dicts.get(REFERENCE_LANG), scopeList));

  checkOnly ? verify(files) : write(files);
}

/**
 * Вложенный JSON → плоская карта `scope.путь.до.ключа`.
 *
 * Лист — это либо строка, либо объект плюрализации: у него есть `other`
 * (обязателен во всех языках) и все значения строковые. Всё остальное — ветка.
 *
 * @param {unknown} node
 * @param {string} path
 * @param {string} file
 * @returns {Record<string, unknown>}
 */
function flatten(node, path, file) {
  if (typeof node === 'string') return { [path]: node };

  if (node === null || typeof node !== 'object' || Array.isArray(node)) {
    fail(`${file}: значение \`${path}\` — не строка и не объект`);
  }

  const entries = Object.entries(/** @type {Record<string, unknown>} */ (node));
  if (entries.length === 0) fail(`${file}: пустой объект \`${path}\``);

  if (isPlural(entries)) return { [path]: Object.fromEntries(entries) };

  /** @type {Record<string, unknown>} */
  const result = {};
  for (const [key, value] of entries) {
    if (key.includes('.')) fail(`${file}: точка в имени ключа \`${path}.${key}\``);

    Object.assign(result, flatten(value, `${path}.${key}`, file));
  }

  return result;
}

/** @param {[string, unknown][]} entries */
function isPlural(entries) {
  return (
    entries.some(([key]) => key === 'other') && entries.every(([, value]) => typeof value === 'string')
  );
}

/** @param {Record<string, unknown>} dict */
function renderDict(dict) {
  const body = Object.keys(dict)
    .sort()
    .map(key => `  ${JSON.stringify(key)}: ${JSON.stringify(dict[key])},`)
    .join('\n');

  return `${HEADER}
export default {
${body}
} as const;
`;
}

/**
 * `core` обоих языков — статическим импортом: бутстрап ничего не ждёт,
 * переключение языка мгновенное, сценария «словарь не приехал» не существует.
 * Остальные словари — `() => import()` рядом с ленивым чанком экрана.
 *
 * Карта загрузчиков разложена литералами, а не собрана шаблонной строкой
 * в `import(\`./\${lang}.\${scope}\`)`: сборщик разбирает только статический путь,
 * а по вычисляемому либо тянет в чанк весь каталог, либо не находит модуль вовсе.
 *
 * @param {string[]} langs
 * @param {string[]} scopes
 */
function renderIndex(langs, scopes) {
  const lazyScopes = scopes.filter(scope => scope !== CORE_SCOPE);

  const imports = langs.map(lang => `import ${lang}Core from './${lang}.${CORE_SCOPE}';`).join('\n');

  const core = langs.map(lang => `  ${lang}: ${lang}Core,`).join('\n');

  const loaders = langs
    .map(lang => {
      const entries = lazyScopes
        .map(scope => `    ${JSON.stringify(scope)}: () => import('./${lang}.${scope}'),`)
        .join('\n');

      return entries.length === 0 ? `  ${lang}: {},` : `  ${lang}: {\n${entries}\n  },`;
    })
    .join('\n');

  return `${HEADER}
import { Dict } from '../dict';
import { Lang } from '../lang';
import { LazyScope, Scope } from './i18n-keys';

${imports}

/** Словарь, который лежит в initial-бандле: он нужен на каждом экране */
export const CORE: Readonly<Record<Lang, Dict>> = {
${core}
};

/** Словари экранов — по одному чанку на язык и экран */
export const LOADERS: Readonly<Record<Lang, Readonly<Record<LazyScope, () => Promise<{ default: Dict }>>>>> = {
${loaders}
};

/** Все словари по именам — состав проверяет \`i18n-parity.spec.ts\` */
export const SCOPES: readonly Scope[] = ${JSON.stringify(scopes)};
`;
}

/**
 * Типы ключей: опечатка в `t('core.action.sav')` становится ошибкой компиляции,
 * а не молчаливым `⟦…⟧` в интерфейсе.
 *
 * @param {Map<string, Record<string, unknown>> | undefined} reference
 * @param {string[]} scopes
 */
function renderKeys(reference, scopes) {
  /** @type {string[]} */
  const plain = [];
  /** @type {string[]} */
  const plural = [];

  for (const dict of reference?.values() ?? []) {
    for (const [key, value] of Object.entries(dict)) {
      (typeof value === 'string' ? plain : plural).push(key);
    }
  }

  const union = (/** @type {string[]} */ keys) =>
    keys.length === 0 ? '  never' : keys.sort().map(key => `  | ${JSON.stringify(key)}`).join('\n');

  const lazy = scopes.filter(scope => scope !== CORE_SCOPE);

  return `${HEADER}
/** Ключ обычной строки — то, что принимает \`I18nService.t\` */
export type I18nKey =
${union(plain)};

/** Ключ строки с числом — то, что принимает \`I18nService.plural\` */
export type I18nPluralKey =
${union(plural)};

/** Имя словаря = первый сегмент ключа = имя папки маршрута */
export type Scope = ${scopes.map(s => JSON.stringify(s)).join(' | ')};

/** Словари, которые едут отдельным чанком: всё, кроме \`${CORE_SCOPE}\` */
export type LazyScope = ${lazy.length === 0 ? 'never' : lazy.map(s => JSON.stringify(s)).join(' | ')};
`;
}

/** @param {Map<string, string>} files */
function write(files) {
  if (existsSync(OUT_DIR)) rmSync(OUT_DIR, { recursive: true });
  mkdirSync(OUT_DIR, { recursive: true });

  for (const [name, content] of files) writeFileSync(join(OUT_DIR, name), content, 'utf8');

  console.log(`i18n: сгенерировано ${files.size} файлов в src/app/shared/i18n/generated`);
}

/** @param {Map<string, string>} files */
function verify(files) {
  /** @type {string[]} */
  const stale = [];

  const existing = existsSync(OUT_DIR) ? readdirSync(OUT_DIR) : [];
  for (const name of existing) {
    if (!files.has(name)) stale.push(`лишний файл: ${name}`);
  }

  for (const [name, content] of files) {
    const path = join(OUT_DIR, name);
    if (!existsSync(path)) stale.push(`нет файла: ${name}`);
    else if (readFileSync(path, 'utf8') !== content) stale.push(`отстал от i18n/: ${name}`);
  }

  if (stale.length > 0) {
    fail(`сгенерированное не совпадает с закоммиченным.\n  ${stale.join('\n  ')}\n  Лечится \`npm run i18n\``);
  }

  console.log('i18n: сгенерированное совпадает с закоммиченным');
}

/** @param {string} message @returns {never} */
function fail(message) {
  console.error(`i18n: ${message}`);
  process.exit(1);
}
