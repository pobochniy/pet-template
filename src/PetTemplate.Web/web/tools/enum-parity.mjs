// @ts-check
/**
 * Перечисления и правила, скопированные с сервера руками, — против исходника на C#.
 *
 * Клиент получает перечисления числами (`System.Text.Json` по умолчанию пишет число),
 * а имена у него свои — копии `src/PetTemplate.Shared/Enums/*.cs` в `*.enum.ts`, моделях
 * и `projects/admin/src/app/core`. Разъехались номера — клиент молча показывает не тот
 * статус и шлёт не то право; компилятор и спеки этого не видят: они сверяют клиент
 * с самим собой. Здесь вторая сторона — разбор `*.cs`:
 *
 *   1. **Перечисления** из таблицы `ENUMS`: каждое имя клиента есть на сервере с тем же
 *      номером, и каждое имя сервера есть на клиенте — кроме перечисленных в `omit`
 *      (`None` у флагов, который клиенту не приходит). Перечисление клиента, которого
 *      нет в таблице, — тоже ошибка: новое должно встать под проверку, а не мимо неё.
 *   2. **Таблицы по номерам** (`RECORDS`): ключи `Record<number, …>` — номера сервера.
 *   3. **Правила аккаунта** (`shared/form-models/account-rules.ts`): длины логина, пароля
 *      и почты и регулярные выражения — те же, что в `UserNameRule`, `PasswordRule`,
 *      `PhoneRule` и `AccountSaveDto`.
 *
 * Поля DTO сверяет генерация типов из OpenAPI (`npm run api:types`, `tools/api-types.mjs`):
 * там перечисления — номера без имён, поэтому имена остаются здесь.
 *
 * Запуск: `npm run enum:parity`. Рядом с `i18n:codes` — в `.githooks/pre-commit`
 * (на правку `*.cs` и клиента) и в воротах `web-tests.yml`.
 */

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const REPO = join(ROOT, '..', '..', '..');
const SERVER_DIRS = ['Shared', 'Api', 'Services', 'Db'].map(dir => join(REPO, 'src', `PetTemplate.${dir}`));
const CLIENT_DIRS = [join(ROOT, 'src'), join(ROOT, 'projects')];

/**
 * Копии перечислений: файл клиента, имя на клиенте, имя на сервере, имена сервера,
 * которых у клиента нет намеренно
 *
 * @type {{ ts: string; name: string; cs?: string; omit?: string[] }[]}
 */
const ENUMS = [
  { ts: 'src/app/shared/enums/permission.enum.ts', name: 'PermissionEnum' },
  { ts: 'src/app/shared/enums/invitation-status.enum.ts', name: 'InvitationStatusEnum' },
  { ts: 'src/app/shared/enums/external-provider.enum.ts', name: 'ExternalProviderEnum', cs: 'ExternalProvider' },
  { ts: 'src/app/shared/models/assistant.model.ts', name: 'AssistantMessageRole' },
  { ts: 'src/app/shared/models/assistant.model.ts', name: 'AssistantPlanStatus' },
  { ts: 'src/app/shared/models/assistant.model.ts', name: 'AssistantRunStatus' },
  { ts: 'src/app/shared/models/assistant.model.ts', name: 'AssistantStopReason' },
  { ts: 'src/app/shared/models/assistant.model.ts', name: 'AssistantFactKind' },
  // Флаговый на сервере; `None` — «ролей нет», клиенту роли приходят массивом по одной
  { ts: 'projects/admin/src/app/core/admin-role.ts', name: 'AdminRole', omit: ['None'] },
  { ts: 'projects/admin/src/app/core/admin-invitation-status.ts', name: 'AdminInvitationStatus' },
  // `None` — пустое значение базы, запуска с ним не бывает
  { ts: 'projects/admin/src/app/core/admin-job.ts', name: 'AdminJobKind', omit: ['None'] },
  { ts: 'projects/admin/src/app/core/admin-job.ts', name: 'AdminJobStatus' },
  { ts: 'projects/admin/src/app/core/assistant-run-status.ts', name: 'AssistantRunStatus' },
  { ts: 'projects/admin/src/app/core/assistant-run-status.ts', name: 'AssistantStopReason' },
  { ts: 'projects/admin/src/app/core/assistant-run-status.ts', name: 'DialogStopReason' },
  { ts: 'projects/admin/src/app/core/provider-ledger-kind.ts', name: 'ProviderLedgerKind' },
  { ts: 'projects/admin/src/app/core/service-spend-kind.ts', name: 'ServiceSpendKind' }
];

/**
 * Таблицы `Record<number, …>` по номерам серверного перечисления — без своего `enum`
 *
 * @type {{ ts: string; name: string; cs: string }[]}
 */
const RECORDS = [
  { ts: 'projects/admin/src/app/accounts/action-titles.ts', name: 'ACTION_TITLES', cs: 'AccountAdminActionKind' }
];

const ACCOUNT_RULES = 'src/app/shared/form-models/account-rules.ts';

main();

function main() {
  const server = readServer();
  /** @type {string[]} */
  const problems = [];

  checkEnums(server.enums, problems);
  checkRecords(server.enums, problems);
  checkAccountRules(server.texts, problems);

  if (problems.length > 0) fail(`копии сервера разошлись (${problems.length}):\n  ${problems.join('\n  ')}`);

  console.log(`enum-parity: ${ENUMS.length} перечислений, ${RECORDS.length} таблиц и правила аккаунта совпадают с сервером`);
}

// ── Перечисления ──────────────────────────────────────────────────────────────

/** @param {Map<string, { file: string; members: Map<string, number> }>} serverEnums @param {string[]} problems */
function checkEnums(serverEnums, problems) {
  const listed = new Set(ENUMS.map(x => `${x.ts}#${x.name}`));

  for (const file of CLIENT_DIRS.flatMap(dir => [...walk(dir, name => name.endsWith('.ts') && !name.endsWith('.spec.ts'))])) {
    const path = relative(ROOT, file).split('\\').join('/');
    for (const match of stripComments(readFileSync(file, 'utf8')).matchAll(/\benum\s+(\w+)\s*\{/g)) {
      if (!listed.has(`${path}#${match[1]}`)) {
        problems.push(`${path}: перечисление \`${match[1]}\` не стоит в таблице ENUMS (tools/enum-parity.mjs) — допишите его копией сервера`);
      }
    }
  }

  for (const entry of ENUMS) {
    const csName = entry.cs ?? entry.name;
    const client = parseEnum(readFileSync(join(ROOT, entry.ts), 'utf8'), entry.name, entry.ts);
    const original = serverEnums.get(csName);

    if (original == null) {
      problems.push(`${entry.ts}: \`${entry.name}\` — на сервере нет \`enum ${csName}\``);
      continue;
    }

    const where = `${entry.ts}#${entry.name} ↔ ${original.file}`;
    for (const [name, value] of client) {
      const expected = original.members.get(name);
      if (expected === undefined) problems.push(`${where}: \`${name}\` нет на сервере`);
      else if (expected !== value) problems.push(`${where}: \`${name}\` = ${value}, на сервере ${expected}`);
    }

    for (const [name, value] of original.members) {
      if (!client.has(name) && !(entry.omit ?? []).includes(name)) {
        problems.push(`${where}: \`${name} = ${value}\` есть на сервере, на клиенте нет`);
      }
    }
  }
}

/** @param {Map<string, { file: string; members: Map<string, number> }>} serverEnums @param {string[]} problems */
function checkRecords(serverEnums, problems) {
  for (const entry of RECORDS) {
    const text = readFileSync(join(ROOT, entry.ts), 'utf8');
    const body = new RegExp(`\\b${entry.name}\\b[^=]*=\\s*\\{([\\s\\S]*?)\\}`).exec(text)?.[1];
    const original = serverEnums.get(entry.cs);

    if (body == null) fail(`${entry.ts}: не найдена таблица \`${entry.name}\` — сломан разбор`);
    if (original == null) {
      problems.push(`${entry.ts}: \`${entry.name}\` — на сервере нет \`enum ${entry.cs}\``);
      continue;
    }

    const keys = new Set([...body.matchAll(/^\s*(\d+)\s*:/gm)].map(x => Number(x[1])));
    const values = new Set(original.members.values());
    const where = `${entry.ts}#${entry.name} ↔ ${original.file}`;

    for (const key of keys) if (!values.has(key)) problems.push(`${where}: номера ${key} на сервере нет`);
    for (const [name, value] of original.members) {
      if (!keys.has(value)) problems.push(`${where}: \`${name} = ${value}\` без подписи на клиенте`);
    }
  }
}

/**
 * Тело `enum Name { … }` → имя → номер. Без явного номера — предыдущий плюс один,
 * как в C# и TypeScript
 *
 * @param {string} text @param {string} name @param {string} file
 * @returns {Map<string, number>}
 */
function parseEnum(text, name, file) {
  const match = new RegExp(`\\benum\\s+${name}\\s*(?::\\s*\\w+\\s*)?\\{([\\s\\S]*?)\\}`).exec(text);
  if (match == null) fail(`${file}: не найдено \`enum ${name}\` — сломан разбор`);

  const body = stripComments(match[1]).replace(/\[[^\]]*\]/g, '');
  /** @type {Map<string, number>} */
  const members = new Map();
  let next = 0;

  for (const raw of body.split(',')) {
    const item = raw.trim();
    if (item === '') continue;

    const parts = /^(\w+)\s*(?:=\s*(.+))?$/.exec(item);
    if (parts == null) fail(`${file}: \`${name}\` — не разобран элемент \`${item}\``);

    const value = parts[2] === undefined ? next : evaluate(parts[2], members, `${file}#${name}`);
    members.set(parts[1], value);
    next = value + 1;
  }

  if (members.size === 0) fail(`${file}: \`${name}\` пустое — сломан разбор`);

  return members;
}

/**
 * Номер элемента: десятичный, шестнадцатеричный, сдвиг или ссылка на соседний элемент
 *
 * @param {string} expression @param {Map<string, number>} members @param {string} where
 */
function evaluate(expression, members, where) {
  const text = expression.trim();

  if (/^-?\d+$/.test(text)) return Number(text);
  if (/^0x[0-9a-f]+$/i.test(text)) return Number.parseInt(text, 16);

  const shift = /^(\d+)\s*<<\s*(\d+)$/.exec(text);
  if (shift) return Number(shift[1]) << Number(shift[2]);

  const known = members.get(text);
  if (known !== undefined) return known;

  return fail(`${where}: номер \`${text}\` не разобран — допишите разбор в tools/enum-parity.mjs`);
}

// ── Правила аккаунта ──────────────────────────────────────────────────────────

/** @param {Map<string, string>} texts имя класса C# → текст файла @param {string[]} problems */
function checkAccountRules(texts, problems) {
  const client = readFileSync(join(ROOT, ACCOUNT_RULES), 'utf8');

  const number = (/** @type {string} */ name) => {
    const value = new RegExp(`const\\s+${name}\\s*=\\s*(\\d+)`).exec(client)?.[1];
    if (value === undefined) fail(`${ACCOUNT_RULES}: не найдена константа \`${name}\``);
    return Number(value);
  };

  const regex = (/** @type {string} */ name) => {
    const found = new RegExp(`const\\s+${name}\\s*=\\s*\\/(.+)\\/([a-z]*);`).exec(client);
    if (found == null) fail(`${ACCOUNT_RULES}: не найдено выражение \`${name}\``);
    return { source: found[1], ignoreCase: found[2].includes('i') };
  };

  const csClass = (/** @type {string} */ name) => {
    const text = texts.get(name);
    if (text == null) fail(`на сервере нет класса \`${name}\``);
    return text;
  };

  const csConst = (/** @type {string} */ cls, /** @type {string} */ name) => {
    const value = new RegExp(`const\\s+int\\s+${name}\\s*=\\s*(\\d+)`).exec(csClass(cls))?.[1];
    if (value === undefined) fail(`\`${cls}.${name}\` не найдена — сломан разбор`);
    return Number(value);
  };

  /** `[GeneratedRegex("…", RegexOptions…)] … Regex Method()` */
  const csRegex = (/** @type {string} */ cls, /** @type {string} */ method) => {
    const found = new RegExp(`\\[GeneratedRegex\\((@?)"((?:[^"\\\\]|\\\\.|"")*)"([^\\]]*)\\)\\]\\s*[\\w\\s]*Regex\\s+${method}\\s*\\(`)
      .exec(csClass(cls));
    if (found == null) fail(`\`${cls}.${method}\` не найдено — сломан разбор`);
    const source = found[1] === '@' ? found[2].replace(/""/g, '"') : found[2].replace(/\\\\/g, '\\');
    return { source, ignoreCase: found[3].includes('IgnoreCase') };
  };

  const same = (/** @type {string} */ what, /** @type {unknown} */ ours, /** @type {unknown} */ theirs) => {
    if (JSON.stringify(ours) !== JSON.stringify(theirs)) {
      problems.push(`${ACCOUNT_RULES}: ${what} — у клиента ${JSON.stringify(ours)}, на сервере ${JSON.stringify(theirs)}`);
    }
  };

  same('USER_NAME_MIN_LENGTH ↔ UserNameRule.MinLength', number('USER_NAME_MIN_LENGTH'), csConst('UserNameRule', 'MinLength'));
  same('USER_NAME_MAX_LENGTH ↔ UserNameRule.MaxLength', number('USER_NAME_MAX_LENGTH'), csConst('UserNameRule', 'MaxLength'));
  same('PASSWORD_MIN_LENGTH ↔ PasswordRule.MinLength', number('PASSWORD_MIN_LENGTH'), csConst('PasswordRule', 'MinLength'));
  same('PASSWORD_MAX_LENGTH ↔ PasswordRule.MaxLength', number('PASSWORD_MAX_LENGTH'), csConst('PasswordRule', 'MaxLength'));
  same('USER_NAME_FORMAT ↔ UserNameRule.Format', regex('USER_NAME_FORMAT'), csRegex('UserNameRule', 'Format'));
  same('USER_NAME_RESERVED ↔ UserNameRule.Reserved', regex('USER_NAME_RESERVED'), csRegex('UserNameRule', 'Reserved'));
  same('PHONE_FORMAT ↔ PhoneRule.Format', regex('PHONE_FORMAT'), csRegex('PhoneRule', 'Format'));

  const email = /\[MaxLength\((\d+)\)\]\s*public\s+string\??\s+Email\b/.exec(csClass('AccountSaveDto'))?.[1];
  if (email === undefined) fail('`AccountSaveDto.Email` без `[MaxLength]` — сломан разбор');
  same('EMAIL_MAX_LENGTH ↔ AccountSaveDto.Email [MaxLength]', number('EMAIL_MAX_LENGTH'), Number(email));
}

// ── Сервер ────────────────────────────────────────────────────────────────────

/**
 * Все `enum` сервера и тексты классов (по имени класса) — для правил аккаунта
 */
function readServer() {
  /** @type {Map<string, { file: string; members: Map<string, number> }>} */
  const enums = new Map();
  /** @type {Map<string, string>} */
  const texts = new Map();

  for (const dir of SERVER_DIRS) {
    for (const file of walk(dir, name => name.endsWith('.cs'))) {
      const text = readFileSync(file, 'utf8');
      const code = stripComments(text);
      const where = relative(REPO, file).split('\\').join('/');

      for (const match of code.matchAll(/\benum\s+(\w+)\s*(?::\s*\w+\s*)?\{/g)) {
        if (enums.has(match[1])) fail(`\`enum ${match[1]}\` объявлен на сервере дважды — сверять не с чем`);
        enums.set(match[1], { file: where, members: parseEnum(code, match[1], where) });
      }

      for (const match of text.matchAll(/\bclass\s+(\w+)/g)) texts.set(match[1], text);
    }
  }

  if (enums.size === 0) fail('на сервере не нашлось ни одного enum — сломан разбор');

  return { enums, texts };
}

// ── Мелочи ────────────────────────────────────────────────────────────────────

/** @param {string} text */
function stripComments(text) {
  return text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '');
}

/**
 * Файлы по дереву; `bin`, `obj`, `node_modules` и снимки миграций пропускаем
 *
 * @param {string} dir @param {(name: string) => boolean} accept @returns {Generator<string>}
 */
function* walk(dir, accept) {
  for (const entry of readdirSync(dir)) {
    if (['bin', 'obj', 'node_modules', 'Migrations', 'generated'].includes(entry)) continue;

    const path = join(dir, entry);

    if (statSync(path).isDirectory()) yield* walk(path, accept);
    else if (accept(entry)) yield path;
  }
}

/** @param {string} message @returns {never} */
function fail(message) {
  console.error(`enum-parity: ${message}`);
  process.exit(1);
}
