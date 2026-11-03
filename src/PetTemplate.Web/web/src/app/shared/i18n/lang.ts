import { safeStorage } from '../core/safe-storage';

/**
 * Язык интерфейса.
 *
 * Хранится **кодом ISO 639-1 строкой**, а не перечислением: код нужен всем
 * потребителям как есть — `Intl.*`, `documentElement.lang`, `navigator.languages`,
 * имя папки словаря. Литеральное объединение строже перечисления и не требует
 * таблицы перевода в код локали.
 *
 * Список поддерживаемых языков здесь **единственный на клиенте**. Флаги живут
 * в `flags/lang-flag.component.ts`.
 */

export type Lang = 'ru' | 'en';

/** Язык, который получает пользователь, когда браузер не назвал ни одного поддерживаемого */
export const DEFAULT_LANG: Lang = 'ru';

/** Ключ в `localStorage`, где лежит выбор языка */
export const LANG_STORAGE_KEY = 'lang';

export interface LangMeta {
  readonly code: Lang;

  /** Самоназвание — им подписан флаг в переключателе */
  readonly nativeName: string;

  /** Английское название — по нему тоже ищет фильтр переключателя */
  readonly englishName: string;
}

export const SUPPORTED_LANGUAGES: readonly LangMeta[] = [
  { code: 'ru', nativeName: 'Русский', englishName: 'Russian' },
  { code: 'en', nativeName: 'English', englishName: 'English' }
];

export function isLang(value: unknown): value is Lang {
  return typeof value === 'string' && SUPPORTED_LANGUAGES.some(x => x.code === value);
}

export function langMeta(lang: Lang): LangMeta {
  return SUPPORTED_LANGUAGES.find(x => x.code === lang)!;
}

/**
 * Первый поддерживаемый язык из списка браузера.
 *
 * Сопоставляем **по префиксу до дефиса**: `en-GB` и `en-US` — один и тот же
 * словарь, регионы мы не различаем. Первое совпадение выигрывает: в
 * `navigator.languages` языки идут по убыванию предпочтения.
 */
export function matchLang(codes: readonly string[] | undefined): Lang | null {
  for (const code of codes ?? []) {
    const primary = code.split('-')[0]?.toLowerCase();
    if (isLang(primary)) return primary;
  }

  return null;
}

/**
 * Язык при старте приложения. Первое найденное выигрывает:
 *
 * 1. `localStorage['lang']` — прошлый выбор в этом браузере;
 * 2. `navigator.languages`;
 * 3. `ru`.
 *
 * Язык профиля на сервере (`Profile.Language`, спец. accounts, 8.4) сюда не читается:
 * его привозит ответ входа, и экран входа переключает интерфейс на него — дальше
 * он лежит в `localStorage`, как любой выбор.
 */
export function resolveInitialLang(): Lang {
  return fromStorage() ?? matchLang(navigator.languages) ?? DEFAULT_LANG;
}

/** Хранилище бывает закрыто целиком — `safeStorage` отвечает `null`, а не бросает */
function fromStorage(): Lang | null {
  const stored = safeStorage.get(LANG_STORAGE_KEY);

  return isLang(stored) ? stored : null;
}
