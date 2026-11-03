// @ts-check
/**
 * Хранилище браузера — только через `safeStorage` (`src/app/shared/core/safe-storage.ts`).
 *
 * Прямое обращение к `localStorage`/`sessionStorage` бросает `SecurityError`, когда
 * хранилище закрыто: приватный режим, запрет сторонних данных, фрейм mini-app VK.
 * Обёртка ловит отказ один раз и держит значение в памяти страницы, а голый вызов
 * в сервисе ронял бы старт приложения. Проверка ищет `localStorage.`, `sessionStorage.`
 * и `window.localStorage`/`window['localStorage']` в `*.ts` продукта и админки.
 *
 * Не проверяются:
 *   - сама обёртка;
 *   - спеки (`*.spec.ts`): они готовят и читают состояние хранилища напрямую — так
 *     видно, что именно лежит в браузере; с ними — общая настройка тестов
 *     `src/test-setup.ts` (язык и сессия перед каждым тестом);
 *   - `src/index.html`: инлайн-скрипт идёт до старта Angular и ловит отказ сам.
 *
 * Комментарии пропускаются: в них хранилище называют словами.
 *
 * Запуск: `npm run lint:storage`. Рядом с `i18n:lint` — в `.githooks/pre-commit`
 * и в воротах `web-tests.yml`.
 */

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const DIRS = [join(ROOT, 'src'), join(ROOT, 'projects')];
const WRAPPER = join('src', 'app', 'shared', 'core', 'safe-storage.ts');
const SKIPPED = new Set([WRAPPER, join('src', 'test-setup.ts')].map(path => path.split(sep).join('/')));

const FORBIDDEN = /\b(?:localStorage|sessionStorage)\s*(?:\.|\?\.|\[)|\bwindow\s*(?:\.\s*|\[\s*['"])(?:localStorage|sessionStorage)\b/;

main();

function main() {
  /** @type {string[]} */
  const found = [];
  let checked = 0;

  for (const dir of DIRS) {
    for (const file of walk(dir)) {
      const path = relative(ROOT, file);
      if (SKIPPED.has(path.split(sep).join('/'))) continue;

      checked++;
      const lines = stripComments(readFileSync(file, 'utf8')).split('\n');
      lines.forEach((line, index) => {
        if (FORBIDDEN.test(line)) found.push(`${path}:${index + 1}: ${line.trim()}`);
      });
    }
  }

  if (found.length > 0) {
    console.error(
      `storage-lint: прямое обращение к хранилищу браузера (${found.length}) — только через \`safeStorage\` ` +
      `(src/app/shared/core/safe-storage.ts):\n  ${found.join('\n  ')}`
    );
    process.exit(1);
  }

  console.log(`storage-lint: ${checked} файлов, хранилище — только через safeStorage`);
}

/**
 * Комментарии — вон, строки остаются на своих местах (номера строк в отчёте верные).
 * Разбор грубый: `//` внутри строкового литерала тоже обрежет хвост строки — проверка
 * от этого только мягче, ложной тревоги не будет
 *
 * @param {string} text
 */
function stripComments(text) {
  return text
    .replace(/\/\*[\s\S]*?\*\//g, block => block.replace(/[^\n]/g, ' '))
    .replace(/(^|[^:'"`])\/\/.*$/gm, '$1');
}

/** @param {string} dir @returns {Generator<string>} */
function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    if (entry === 'node_modules' || entry === 'generated') continue;

    const path = join(dir, entry);

    if (statSync(path).isDirectory()) yield* walk(path);
    else if (entry.endsWith('.ts') && !entry.endsWith('.spec.ts')) yield path;
  }
}
