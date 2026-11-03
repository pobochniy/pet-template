// @ts-check
/**
 * Типы DTO клиента — из OpenAPI документа API, а не копиями руками.
 *
 * Документ снимается со сборки, а не с живого сервера: `dotnet swagger tofile`
 * (Swashbuckle CLI, локальный инструмент в `dotnet-tools.json`) поднимает хост API
 * до `Build()` и не запускает его — ни база, ни Qdrant не нужны, поэтому проверка
 * работает в CI и хуке. Из документа `openapi-typescript` делает **только типы**
 * (`.d.ts`, в бандл не попадает ни байта): клиент запросов не генерируется, свой
 * `BaseApiService` остаётся. Модели клиента (`src/app/shared/models/*.ts`,
 * `projects/admin/src/app/core/admin.model.ts`) — алиасы `ApiSchema<'ProfileDto'>`.
 *
 * Обязательность полей. Swashbuckle пишет `nullable` по аннотациям C#
 * (`SupportNonNullableReferenceTypes`) и `required` — ненулевым ссылочным
 * (`NonNullableReferenceTypesAsRequired`): так их требует привязка модели MVC. Для **тела
 * запроса** это и есть правда — пропущенное `bool` или `string?` сервер примет. Для
 * **ответа** — нет: `System.Text.Json` пишет каждое свойство (глобального `WhenWritingNull`
 * у API нет), поэтому у схем, которые встречаются в ответах, обязательно всё, а `null`
 * остаётся в типе (`email: string | null`). Схема и в запросе, и в ответе (`ProfileDto`,
 * лимит ассистента) считается ответной: читать её клиенту важнее, а тело он и так шлёт целиком.
 *
 * Перечисления OpenAPI отдаёт числами без имён — в типах они объединения номеров
 * (`0 | 1 | 2`), совместимые с `enum` клиента в обе стороны; имена сверяет `enum:parity`.
 *
 * Запуск: `npm run api:types` — пересобрать API и переписать
 * `src/app/shared/api/generated/schema.d.ts` (он коммитится). Проверка:
 * `npm run api:types -- --check` — сверить с закоммиченным, ничего не писать
 * (`.githooks/pre-commit` на правку `*.cs`/`*.csproj`, `web-tests.yml`).
 * `--no-build` — не собирать API (хук уже собрал решение).
 *
 * Версия `swashbuckle.aspnetcore.cli` в `dotnet-tools.json` — та же, что у пакетов
 * `Swashbuckle.AspNetCore.*` в `Directory.Packages.props`: поднимаете одно — поднимайте и другое.
 */

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import openapiTS, { astToString } from 'openapi-typescript';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const REPO = join(ROOT, '..', '..', '..');
const API = join(REPO, 'src', 'PetTemplate.Api');
const PROJECT = join(API, 'PetTemplate.Api.csproj');
const OUTPUT = join(ROOT, 'src', 'app', 'shared', 'api', 'generated', 'schema.d.ts');

const checkOnly = process.argv.includes('--check');
const noBuild = process.argv.includes('--no-build');

const HEADER = `/**
 * Сгенерировано \`npm run api:types\` (tools/api-types.mjs) из OpenAPI документа PetTemplate.Api.
 * Руками не править: правка DTO на сервере — и перегенерировать.
 */

`;

const FOOTER = `
/** Схема DTO по имени: \`ApiSchema<'ProfileDto'>\` */
export type ApiSchema<Name extends keyof components['schemas']> = components['schemas'][Name];
`;

const document = readOpenApi();
markRequired(document);

const ast = await openapiTS(/** @type {any} */ (document), {
  // Перечисления — объединения номеров, а не `enum`: `enum` клиента живут свои (`enum:parity`)
  enum: false,
  additionalProperties: false,
  alphabetize: false
});
const generated = HEADER + astToString(ast) + FOOTER;

const name = relative(ROOT, OUTPUT);

if (checkOnly) {
  const committed = existsSync(OUTPUT) ? readFileSync(OUTPUT, 'utf8') : '';
  if (committed !== generated) {
    console.error(`api-types: ${name} разошёлся с OpenAPI документом API — запустите \`npm run api:types\` и закоммитьте`);
    process.exit(1);
  }
  console.log(`api-types: ${name} совпадает с API`);
} else {
  writeFileSync(OUTPUT, generated);
  console.log(`api-types: ${name} записан`);
}

/** Собрать API и снять с него документ `v1` */
function readOpenApi() {
  const dotnet = (/** @type {string[]} */ args, /** @type {string} */ cwd) =>
    execFileSync('dotnet', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] });

  if (!noBuild) dotnet(['build', PROJECT, '-v', 'q', '--nologo'], REPO);

  const dll = dotnet(['msbuild', PROJECT, '-getProperty:TargetPath'], REPO).trim();
  const dir = mkdtempSync(join(tmpdir(), 'api-types-'));
  const file = join(dir, 'swagger.json');

  try {
    dotnet(['tool', 'restore'], REPO);
    // Каталог API — корень содержимого хоста: оттуда читается `appsettings.json`, без него
    // `ConfigureServices` падает на проверке ключей подписи. Окружение — продовое,
    // чтобы локальный `ASPNETCORE_ENVIRONMENT` не менял документ
    execFileSync('dotnet', ['swagger', 'tofile', '--output', file, dll, 'v1'], {
      cwd: API,
      stdio: ['ignore', 'ignore', 'inherit'],
      env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', DOTNET_ENVIRONMENT: 'Production' }
    });
    return JSON.parse(readFileSync(file, 'utf8'));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

/**
 * `required` у схем, которые встречаются в ответах (с обходом ссылок вглубь), — все свойства
 * (см. шапку). Схемы только тел запросов и параметров остаются как их описал Swashbuckle
 *
 * @param {any} document
 */
function markRequired(document) {
  const schemas = document.components?.schemas ?? {};
  const responseSchemas = new Set();

  /** @param {any} node */
  const collect = node => {
    if (node == null || typeof node !== 'object') return;
    if (Array.isArray(node)) return node.forEach(collect);

    if (typeof node.$ref === 'string') {
      const name = node.$ref.replace('#/components/schemas/', '');
      if (responseSchemas.has(name)) return;
      responseSchemas.add(name);
      collect(schemas[name]);
    }
    for (const value of Object.values(node)) collect(value);
  };

  for (const path of Object.values(document.paths ?? {})) {
    for (const operation of Object.values(/** @type {any} */ (path))) collect(operation?.responses);
  }

  for (const [name, schema] of Object.entries(schemas)) {
    if (responseSchemas.has(name) && schema.properties) schema.required = Object.keys(schema.properties);
  }
}
