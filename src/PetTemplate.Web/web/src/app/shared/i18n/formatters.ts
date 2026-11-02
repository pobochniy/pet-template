/**
 * Кеш форматтеров `Intl`.
 *
 * Создание `Intl.DateTimeFormat` — самая дорогая операция во всём `Intl`:
 * разбор набора опций и подъём данных локали. В проекте они создавались
 * на каждый вызов — в цикле по ячейкам списка это сотни экземпляров на кадр.
 * С переключением языка на ходу константой не обойтись, поэтому кеш с ключом
 * по языку и опциям.
 *
 * Кеш не чистится: ключей столько же, сколько разных наборов опций в коде,
 * умножить на число языков — десятки, а не рост со временем.
 */

import { Lang } from './lang';

/**
 * Язык интерфейса → локаль для `Intl`.
 *
 * `en` у нас означает **`en-GB`** (как у Lad и ASU, «Региональные
 * конвенции»): рынок не американский, а голый `en` для `Intl` — это `en-US`,
 * то есть `1/5/26` вместо `5/1/26` и `3:45 PM` вместо `15:45`. Порядок дня
 * и месяца в коротких датах не косметика: `1/5` читается и как 1 мая, и как
 * 5 января.
 *
 * Таблица заодно — единственное место, которое придётся править, когда язык
 * и регион разъедутся (`en-US` рядом с `en-GB`): локаль берётся только отсюда,
 * а `Lang` остаётся кодом языка для `documentElement.lang`, `navigator.languages`
 * и имени папки словаря.
 */
const INTL_LOCALES: Record<Lang, string> = {
  ru: 'ru',
  en: 'en-GB'
};

const dateTimeCache = new Map<string, Intl.DateTimeFormat>();
const relativeTimeCache = new Map<string, Intl.RelativeTimeFormat>();
const pluralRulesCache = new Map<string, Intl.PluralRules>();
const collatorCache = new Map<string, Intl.Collator>();
const numberCache = new Map<string, Intl.NumberFormat>();

export function dateTimeFormat(lang: Lang, options: Intl.DateTimeFormatOptions = {}): Intl.DateTimeFormat {
  return fromCache(dateTimeCache, lang, options, () =>
    new Intl.DateTimeFormat(locale(lang), options)
  );
}

export function relativeTimeFormat(
  lang: Lang,
  options: Intl.RelativeTimeFormatOptions = {}
): Intl.RelativeTimeFormat {
  return fromCache(relativeTimeCache, lang, options, () =>
    new Intl.RelativeTimeFormat(locale(lang), options)
  );
}

export function pluralRules(lang: Lang, options: Intl.PluralRulesOptions = {}): Intl.PluralRules {
  return fromCache(pluralRulesCache, lang, options, () =>
    new Intl.PluralRules(locale(lang), options)
  );
}

/**
 * Сравнение строк для сортировки. Без него `localeCompare` по умолчанию
 * берёт локаль устройства, и порядок списка зависел бы от настроек системы,
 * а не от языка интерфейса
 */
export function collator(lang: Lang, options: Intl.CollatorOptions = {}): Intl.Collator {
  return fromCache(collatorCache, lang, options, () => new Intl.Collator(locale(lang), options));
}

/** Числа с разрядами по языку: «1 240» по-русски, «1,240» по-английски */
export function numberFormat(lang: Lang, options: Intl.NumberFormatOptions = {}): Intl.NumberFormat {
  return fromCache(numberCache, lang, options, () => new Intl.NumberFormat(locale(lang), options));
}

function locale(lang: Lang): string {
  return INTL_LOCALES[lang];
}

function fromCache<T>(cache: Map<string, T>, lang: Lang, options: object, create: () => T): T {
  // Ключ по языку и опциям: порядок полей в литерале опций в коде постоянен,
  // поэтому `JSON.stringify` здесь устойчив. По языку, а не по локали: соответствие
  // между ними взаимно однозначное, и ключ от локали точнее не станет
  const key = `${lang}|${JSON.stringify(options)}`;

  let value = cache.get(key);
  if (value === undefined) {
    value = create();
    cache.set(key, value);
  }

  return value;
}
