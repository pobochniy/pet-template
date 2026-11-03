/**
 * Разбор узкого markdown — ответов ассистента и статей справки (спец. assistant, 2.2;
 * спец. help, 3.2) — в дерево узлов, которое рисует `MarkdownComponent` шаблоном Angular.
 *
 * HTML здесь не строится вовсе: текст модели и статьи — недоверенный ввод (спец. assistant, 9),
 * поэтому разметка из него становится только узлами дерева, а текст узлов Angular
 * экранирует интерполяцией. Тег `<script>` в ответе модели так и останется текстом.
 *
 * Что умеет: абзацы (одиночный перенос — перенос строки), заголовки `#`…`######`,
 * списки `-`/`*`/`+` и `1.` с вложенностью по отступу, блоки кода ```` ``` ````,
 * **жирный**, *курсив*, `код`, ссылки `[текст](адрес)`. Ссылка — только на свой маршрут
 * (`/путь`); чужой адрес, `//хост`, `javascript:` и прочее остаются текстом — см. `ownRoute`.
 * Чего нет — таблиц, картинок, цитат и сырого HTML: их источники так не пишут,
 * а встретится — покажется текстом как есть
 */

export type MdInline =
  | { readonly kind: 'text'; readonly text: string }
  | { readonly kind: 'break' }
  | { readonly kind: 'code'; readonly text: string }
  | { readonly kind: 'strong'; readonly children: readonly MdInline[] }
  | { readonly kind: 'em'; readonly children: readonly MdInline[] }
  /** Ссылка на свой маршрут: `href` уже проверен `ownRoute` */
  | { readonly kind: 'link'; readonly href: string; readonly children: readonly MdInline[] };

export interface MdListItem {
  readonly inlines: readonly MdInline[];
  /** Вложенный список — строки с отступом под пунктом */
  readonly children: readonly MdList[];
}

export interface MdList {
  readonly kind: 'list';
  readonly ordered: boolean;
  readonly start: number;
  readonly items: readonly MdListItem[];
}

export type MdBlock =
  | { readonly kind: 'paragraph'; readonly inlines: readonly MdInline[] }
  | { readonly kind: 'heading'; readonly level: 1 | 2 | 3 | 4 | 5 | 6; readonly inlines: readonly MdInline[] }
  | { readonly kind: 'code'; readonly text: string }
  | MdList;

const FENCE = /^ {0,3}(`{3,}|~{3,})/;
const HEADING = /^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/;
const LIST_ITEM = /^( *)([-*+]|\d{1,9}[.)])\s+(.*)$/;

export function parseMarkdown(source: string): MdBlock[] {
  const lines = source.replace(/\r\n?/g, '\n').split('\n');
  const blocks: MdBlock[] = [];
  let paragraph: string[] = [];

  const flush = () => {
    if (paragraph.length > 0) blocks.push({ kind: 'paragraph', inlines: parseInline(paragraph.join('\n')) });
    paragraph = [];
  };

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];

    const fence = FENCE.exec(line);
    if (fence) {
      flush();
      const marker = fence[1];
      const code: string[] = [];
      // Незакрытый блок идёт до конца текста — так его покажет любой отрисовщик
      while (++i < lines.length && !lines[i].trimStart().startsWith(marker)) code.push(lines[i]);
      blocks.push({ kind: 'code', text: code.join('\n') });
      continue;
    }

    if (line.trim() === '') {
      flush();
      continue;
    }

    const heading = HEADING.exec(line);
    if (heading) {
      flush();
      blocks.push({ kind: 'heading', level: heading[1].length as 1 | 2 | 3 | 4 | 5 | 6, inlines: parseInline(heading[2]) });
      continue;
    }

    // Список начинается и без пустой строки перед ним: справка пишет «В меню:» и сразу пункты
    if (LIST_ITEM.test(line)) {
      flush();
      const end = listEnd(lines, i);
      blocks.push(parseList(lines.slice(i, end)));
      i = end - 1;
      continue;
    }

    paragraph.push(line.trim());
  }

  flush();
  return blocks;
}

/** Строки списка идут до пустой строки, за которой нет ни пункта, ни продолжения с отступом */
function listEnd(lines: readonly string[], from: number): number {
  let i = from;
  while (i < lines.length) {
    const line = lines[i];
    if (line.trim() === '') {
      const next = lines[i + 1];
      if (next === undefined || !(LIST_ITEM.test(next) || /^ {2,}\S/.test(next))) break;
    } else if (FENCE.test(line) || HEADING.test(line)) {
      break;
    }
    i++;
  }
  return i;
}

/**
 * Список из своих строк: пункт — строка с маркером на отступе первого пункта, строки
 * глубже — его вложенный список или продолжение текста
 */
function parseList(lines: readonly string[]): MdList {
  const first = LIST_ITEM.exec(lines[0]);
  // Первая строка — пункт: `parseMarkdown` и рекурсия зовут только с ним
  const indent = first![1].length;
  const ordered = /\d/.test(first![2]);
  const start = ordered ? parseInt(first![2], 10) : 1;

  const items: { text: string[]; nested: string[] }[] = [];

  for (const line of lines) {
    if (line.trim() === '') continue;

    const item = LIST_ITEM.exec(line);
    if (item && item[1].length <= indent) {
      items.push({ text: [item[3].trim()], nested: [] });
      continue;
    }

    const current = items[items.length - 1];
    if (item || current.nested.length > 0) current.nested.push(line);
    else current.text.push(line.trim());
  }

  return {
    kind: 'list',
    ordered,
    start,
    items: items.map(item => ({
      inlines: parseInline(item.text.join('\n')),
      children: item.nested.length > 0 && LIST_ITEM.test(item.nested[0]) ? [parseList(item.nested)] : []
    }))
  };
}

/**
 * Ссылка — только на свой маршрут: путь от `/`, но не `//хост` и не `/\хост` (браузер
 * читает обе как адрес другого сайта). Остальное — `https:`, `javascript:`, `mailto:`,
 * относительные пути — ссылкой не становится
 */
export function ownRoute(href: string): string | null {
  const trimmed = href.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//') || trimmed.startsWith('/\\')) return null;
  // Управляющие символы и пробелы в адресе — не наш маршрут, а попытка его подделать
  if (/[\s\u0000-\u001f\u007f]/.test(trimmed)) return null;
  return trimmed;
}

export function parseInline(source: string): MdInline[] {
  const out: MdInline[] = [];
  let text = '';
  let i = 0;

  const pushText = () => {
    if (text) out.push({ kind: 'text', text });
    text = '';
  };

  while (i < source.length) {
    const ch = source[i];

    if (ch === '\\' && i + 1 < source.length && /[\\`*_[\]()#+\-.!~]/.test(source[i + 1])) {
      text += source[i + 1];
      i += 2;
      continue;
    }

    if (ch === '\n') {
      pushText();
      out.push({ kind: 'break' });
      i++;
      continue;
    }

    if (ch === '`') {
      // Строка начинается с обратной кавычки — совпадение есть всегда
      const ticks = /^`+/.exec(source.slice(i))![0];
      const close = source.indexOf(ticks, i + ticks.length);
      if (close > 0) {
        pushText();
        out.push({ kind: 'code', text: source.slice(i + ticks.length, close).replace(/\n/g, ' ').trim() || ' ' });
        i = close + ticks.length;
        continue;
      }
      text += ticks;
      i += ticks.length;
      continue;
    }

    if (ch === '*' || ch === '_') {
      const span = emphasis(source, i);
      if (span) {
        pushText();
        out.push({ kind: span.strong ? 'strong' : 'em', children: parseInline(span.inner) });
        i = span.end;
        continue;
      }
    }

    if (ch === '[') {
      const link = linkAt(source, i);
      if (link) {
        pushText();
        const href = ownRoute(link.href);
        const children = parseInline(link.label);
        if (href) out.push({ kind: 'link', href, children });
        else {
          // Чужой адрес — текстом: подпись и, если она не сам адрес, адрес в скобках
          out.push(...children);
          if (link.href.trim() !== '' && link.label.trim() !== link.href.trim()) out.push({ kind: 'text', text: ` (${link.href.trim()})` });
        }
        i = link.end;
        continue;
      }
    }

    text += ch;
    i++;
  }

  pushText();
  return out;
}

/**
 * `**жирный**`, `__жирный__`, `*курсив*`, `_курсив_`. Подчёркивание — только на границе
 * слова: `snake_case_name` курсивом не становится. Внутри маркера сразу пробела нет
 * (`2 * 3 * 4` — не курсив)
 */
function emphasis(source: string, at: number): { strong: boolean; inner: string; end: number } | null {
  const ch = source[at];
  const strong = source[at + 1] === ch;
  const marker = strong ? ch + ch : ch;
  const open = at + marker.length;

  if (open >= source.length || /\s/.test(source[open])) return null;
  if (ch === '_' && at > 0 && /[\p{L}\p{N}]/u.test(source[at - 1])) return null;

  let close = source.indexOf(marker, open);
  while (close > 0) {
    const before = source[close - 1];
    const after = source[close + marker.length];
    const boundary = ch !== '_' || after === undefined || !/[\p{L}\p{N}]/u.test(after);
    // `*` одиночного курсива не должен быть половиной `**`
    const single = strong || source[close + 1] !== ch;
    if (!/\s/.test(before) && boundary && single && close > open) {
      return { strong, inner: source.slice(open, close), end: close + marker.length };
    }
    close = source.indexOf(marker, close + 1);
  }
  return null;
}

/** `[подпись](адрес)` — подпись без вложенных скобок, адрес без пробелов (кроме `"title"`) */
function linkAt(source: string, at: number): { label: string; href: string; end: number } | null {
  const match = /^\[([^\]\n]*)\]\(\s*<?([^\s)<>]*)>?(?:\s+"[^"]*")?\s*\)/.exec(source.slice(at));
  if (!match || match[1].trim() === '') return null;
  return { label: match[1], href: match[2], end: at + match[0].length };
}
