import { collator, dateTimeFormat, numberFormat } from './formatters';
import { Lang } from './lang';

/**
 * Даты и сортировка строк на языке интерфейса (`src/PetTemplate.Web/agents.md`, «Локализация»).
 *
 * **`DatePipe` Angular здесь не годится** и заменять эти функции им нельзя:
 * он берёт локаль из `LOCALE_ID`, а тот неизменяем после создания инжектора —
 * переключение языка на лету его не заденет, и дата осталась бы на языке,
 * с которым приложение загрузилось.
 *
 * Язык передаётся **параметром**, а не читается из сервиса: функции зовут
 * и из шаблонов (там под рукой `i18n.lang()`), и из утилит без инжектора.
 * Читая сигнал внутри, они заодно молча подписывали бы на него любой `computed`,
 * который их вызвал.
 *
 * Форматтеры берутся из кеша (`formatters.ts`): создание `Intl.DateTimeFormat` —
 * самая дорогая операция во всём `Intl`, а в цикле по ячейкам списка их были
 * бы сотни на кадр.
 */

/** Пустое значение — пустая строка: подпись «Invalid Date» хуже, чем её отсутствие */
export function formatDate(
  lang: Lang,
  value: Date | string | number | null | undefined,
  options: Intl.DateTimeFormatOptions = { dateStyle: 'medium' }
): string {
  const date = toDate(value);

  return date ? dateTimeFormat(lang, options).format(date) : '';
}

/**
 * Целое число с разрядами на языке интерфейса — границы `Validators.min`/`max`:
 * «1 240», а не «1240». Разделитель разрядов у `Intl` — неразрывный пробел: число
 * не разорвётся переносом строки
 */
export function formatNumber(lang: Lang, value: number, options: Intl.NumberFormatOptions = {}): string {
  return numberFormat(lang, options).format(value);
}

/**
 * Сравнение строк для сортировки списков. `localeCompare` без локали берёт её
 * из настроек системы, и порядок зависел бы от устройства, а не от языка интерфейса
 */
export function compareStrings(lang: Lang, a: string, b: string): number {
  return collator(lang, { sensitivity: 'base', numeric: true }).compare(a, b);
}

function toDate(value: Date | string | number | null | undefined): Date | null {
  if (value == null || value === '') return null;

  const date = value instanceof Date ? value : new Date(value);

  return Number.isNaN(date.getTime()) ? null : date;
}

/**
 * Названия дней недели в нумерации `Date.getDay()` (0 — воскресенье).
 *
 * Берём у `Intl`, а не из словаря: это ровно те же данные локали, что уже едут
 * в браузере, и держать их второй раз в `i18n/` значило бы переводить руками
 * то, что переведено в стандарте. Заодно новый язык получает дни недели
 * бесплатно, без правки словарей.
 *
 * Готовый массив кешируется: его читают шапка недели и календарь месяца
 * на каждой отрисовке.
 */
const weekdayCache = new Map<string, readonly string[]>();

export function weekdayNames(
  lang: Lang,
  weekday: 'narrow' | 'short' | 'long' = 'short'
): readonly string[] {
  const key = `${lang}|${weekday}`;

  let names = weekdayCache.get(key);
  if (names === undefined) {
    const formatter = dateTimeFormat(lang, { weekday });

    // 4 января 1970 — воскресенье, то есть `getDay() === 0`
    names = Array.from({ length: 7 }, (_, day) => formatter.format(new Date(1970, 0, 4 + day)));
    weekdayCache.set(key, names);
  }

  return names;
}
