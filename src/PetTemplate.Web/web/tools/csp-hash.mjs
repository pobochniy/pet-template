// @ts-check
/**
 * Хеш инлайн-скрипта `index.html` в Content-Security-Policy nginx (`IaC/nginx/conf/nginx.conf`).
 *
 * CSP продукта пускает скрипты только своего источника и один инлайн — снятие параметров
 * запуска mini-app в `src/index.html` (идёт до Angular и инлайном остаётся) — по его
 * `sha256-…`. Любая правка скрипта меняет хеш, и со старым браузер скрипт не выполнит:
 * mini-app молча перестанет входить. Поэтому хеш сверяется машиной:
 *
 *   npm run csp:hash              — напечатать хеши инлайн-скриптов;
 *   npm run csp:hash -- --check   — сверить с nginx.conf (ворота `web-tests.yml`, хук `pre-commit`);
 *   npm run csp:hash -- --write   — вписать новый хеш в nginx.conf.
 *
 * Хеш считается от исходного `index.html`: сборка инлайн-скрипт не трогает. Если сборка
 * уже лежит в `dist/` — сверяется и она: те же скрипты и ни одного инлайн-обработчика
 * (`onload="…"`), его CSP не пустит. Обработчик появился бы от инлайна критических стилей —
 * он выключен в `angular.json` (`inlineCritical: false`).
 *
 * У админки инлайн-скриптов нет вовсе: её политика — `script-src 'self'` без хешей.
 * Без зависимостей — идёт до `npm ci`
 */

import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const WEB = join(dirname(fileURLToPath(import.meta.url)), '..');
const REPO = join(WEB, '..', '..', '..');
const NGINX = join(REPO, 'IaC', 'nginx', 'conf', 'nginx.conf');

const SITES = [
  {
    name: 'product',
    variable: '$csp_product',
    source: join(WEB, 'src', 'index.html'),
    built: join(WEB, 'dist', 'pet-template', 'browser', 'index.html')
  },
  {
    name: 'admin',
    variable: '$csp_admin',
    source: join(WEB, 'projects', 'admin', 'src', 'index.html'),
    built: join(WEB, 'dist', 'admin', 'browser', 'index.html')
  }
];

const check = process.argv.includes('--check');
const write = process.argv.includes('--write');
const errors = [];
let nginx = readFileSync(NGINX, 'utf8');

for (const site of SITES) {
  const hashes = inlineScriptHashes(readFileSync(site.source, 'utf8'));
  const declaration = policyOf(nginx, site.variable);

  if (!check && !write) {
    console.log(`${site.name}: ${hashes.length ? hashes.map(h => `'${h}'`).join(' ') : '(инлайн-скриптов нет)'}`);
  }

  if (existsSync(site.built)) {
    const html = readFileSync(site.built, 'utf8');
    const built = inlineScriptHashes(html);
    if (built.join() !== hashes.join()) {
      errors.push(`${rel(site.built)}: инлайн-скрипты сборки не совпадают с ${rel(site.source)} — сборка их изменила, хеш считать от неё`);
    }
    const handler = /<[^>]+\son[a-z]+\s*=/i.exec(html);
    if (handler) errors.push(`${rel(site.built)}: инлайн-обработчик ${handler[0]}… — CSP его не пустит (inlineCritical в angular.json?)`);
  }

  if (!declaration) {
    errors.push(`${rel(NGINX)}: нет \`map $host ${site.variable} { default "…"; }\` с политикой ${site.name}`);
    continue;
  }

  const declared = [...declaration.value.matchAll(/'(sha256-[A-Za-z0-9+/=]+)'/g)].map(m => m[1]).sort();
  if (declared.join() === hashes.join()) continue;

  if (write) {
    const scriptSrc = ["script-src 'self'", ...hashes.map(h => `'${h}'`)].join(' ');
    const value = declaration.value.replace(/script-src [^;]*/, scriptSrc);
    nginx = nginx.split(declaration.value).join(value);
    console.log(`${site.name}: ${scriptSrc}`);
  } else if (check) {
    errors.push(
      `${rel(NGINX)}: ${site.variable} — хеши ${declared.join(' ') || 'нет'}, а инлайн-скрипты ${rel(site.source)} — ${hashes.join(' ') || 'нет'}. ` +
      'Обновить: npm run csp:hash -- --write'
    );
  }
}

if (write) writeFileSync(NGINX, nginx);

if (errors.length > 0) {
  console.error(`csp-hash: CSP не сходится (${errors.length}):`);
  for (const error of errors) console.error(`  ${error}`);
  process.exit(1);
}

if (check) console.log('csp-hash: хеши инлайн-скриптов совпадают с nginx.conf');

/** `sha256-…` содержимого каждого инлайн-`<script>` (без `src`) — так их считает браузер */
function inlineScriptHashes(html) {
  return [...html.matchAll(/<script(\s[^>]*)?>([\s\S]*?)<\/script>/gi)]
    .filter(m => !/\ssrc\s*=/i.test(m[1] ?? ''))
    .map(m => `sha256-${createHash('sha256').update(m[2], 'utf8').digest('base64')}`)
    .sort();
}

/** Значение `map $host $csp_… { default "…"; }` — одна политика на все `server` сайта */
function policyOf(conf, variable) {
  const match = new RegExp(`map\\s+\\$host\\s+\\${variable}\\s*\\{\\s*default\\s+"([^"]*)"\\s*;`).exec(conf);
  return match ? { value: match[1] } : null;
}

function rel(path) {
  return relative(REPO, path);
}
