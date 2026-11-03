import { Dict, PluralForms } from './dict';
import { CORE, LOADERS } from './generated';
import { LazyScope } from './generated/i18n-keys';
import { Lang, SUPPORTED_LANGUAGES } from './lang';

/**
 * Словари всех языков обязаны совпадать по составу ключей (`src/PetTemplate.Web/agents.md`, «Локализация»).
 *
 * Лишний ключ и пропущенный красные одинаково: пропущенный — это `⟦…⟧` в интерфейсе
 * (в проде — обрубок ключа), лишний — перевод, который никто не видит, и признак
 * того, что ключ переименовали в одном языке и забыли в другом.
 *
 * Эталон — `en`: на нём формулировки пишутся первыми.
 *
 * Сверка здесь — только **между словарями**. Кода, которого нет ни в одном из них,
 * она не видит: оба языка согласованно молчат. Вторая сторона — `npm run i18n:codes`
 * (`tools/i18n-server-codes.mjs`): он собирает коды отказов со стороны сервера
 * и требует каждый в `en`. Оба стоят в `web-tests.yml` — воротами деплоя.
 */
const REFERENCE_LANG: Lang = 'en';

describe('словари', () => {
  /** Все словари языка по именам — и `core`, и экранные */
  async function dictsOf(lang: Lang): Promise<Record<string, Dict>> {
    const scopes = Object.keys(LOADERS[lang]) as LazyScope[];
    const loaded = await Promise.all(
      scopes.map(async scope => [scope, (await LOADERS[lang][scope]()).default] as const)
    );

    return { core: CORE[lang], ...Object.fromEntries(loaded) };
  }

  /** Ключи языка, сплющенные в один отсортированный список */
  async function keysOf(lang: Lang): Promise<string[]> {
    const dicts = await dictsOf(lang);

    return Object.values(dicts).flatMap(dict => Object.keys(dict)).sort();
  }

  it('should have the same key set in every language', async () => {
    const reference = await keysOf(REFERENCE_LANG);

    for (const { code } of SUPPORTED_LANGUAGES) {
      if (code === REFERENCE_LANG) continue;

      const keys = await keysOf(code);

      expect(keys.filter(key => !reference.includes(key)), `лишние ключи в ${code}`).toEqual([]);
      expect(reference.filter(key => !keys.includes(key)), `нет ключей в ${code}`).toEqual([]);
    }
  });

  /**
   * Категория, которой в объекте нет, подменяется на `other` — строка получится,
   * но не та: по-русски «5 символа» вместо «5 символов». Поэтому проверяем набор
   * категорий **того самого языка**, а не общий: у `en` их две (`one`, `other`),
   * у `ru` — четыре (`one`, `few`, `many`, `other`).
   */
  it('should cover every plural category of its own language', async () => {
    for (const { code } of SUPPORTED_LANGUAGES) {
      const categories = new Intl.PluralRules(code).resolvedOptions().pluralCategories;
      const dicts = await dictsOf(code);

      for (const [scope, dict] of Object.entries(dicts)) {
        for (const [key, entry] of Object.entries(dict)) {
          if (typeof entry === 'string') continue;

          const missing = categories.filter(category => (entry as PluralForms)[category] == null);

          expect(missing, `${code}: ${scope} → ${key}`).toEqual([]);
        }
      }
    }
  });

  /**
   * `other` — то, что `Intl.PluralRules` отдаёт для всего, не попавшего в остальные
   * категории, и последняя страховка на случай неучтённой. Без него строка
   * не соберётся вовсе
   */
  it('should always have the “other” form', async () => {
    for (const { code } of SUPPORTED_LANGUAGES) {
      const dicts = await dictsOf(code);

      for (const [scope, dict] of Object.entries(dicts)) {
        for (const [key, entry] of Object.entries(dict)) {
          if (typeof entry === 'string') continue;

          expect((entry as PluralForms).other, `${code}: ${scope} → ${key}`).toBeTruthy();
        }
      }
    }
  });

  /**
   * Ключ начинается с имени своего словаря: по первому сегменту `t()` находит,
   * в каком словаре искать. Ключ `home.*` в словаре `notes` не нашёлся бы никогда
   */
  it('should prefix every key with its own scope', async () => {
    for (const { code } of SUPPORTED_LANGUAGES) {
      const dicts = await dictsOf(code);

      for (const [scope, dict] of Object.entries(dicts)) {
        const foreign = Object.keys(dict).filter(key => !key.startsWith(`${scope}.`));

        expect(foreign, `${code}: словарь ${scope}`).toEqual([]);
      }
    }
  });

  /**
   * Подстановки должны совпадать: перевод с лишним `{name}` покажет фигурные
   * скобки как есть, а с пропущенным — молча потеряет имя или число
   */
  it('should use the same placeholders in every language', async () => {
    const reference = await dictsOf(REFERENCE_LANG);

    for (const { code } of SUPPORTED_LANGUAGES) {
      if (code === REFERENCE_LANG) continue;

      const dicts = await dictsOf(code);

      for (const [scope, dict] of Object.entries(reference)) {
        for (const [key, entry] of Object.entries(dict)) {
          const translated = dicts[scope]?.[key];
          if (translated == null) continue;

          expect(placeholders(translated), `${code}: ${key}`).toEqual(placeholders(entry));
        }
      }
    }
  });
});

/** Имена подстановок `{name}` в строке или во всех формах числа, по алфавиту */
function placeholders(entry: string | PluralForms): string[] {
  const texts = typeof entry === 'string' ? [entry] : Object.values(entry);
  const names = new Set<string>();

  for (const text of texts) {
    for (const match of text.matchAll(/\{(\w+)\}/g)) names.add(match[1]);
  }

  // `{n}` подставляет сам `plural()`, в тексте другого языка его может не быть
  names.delete('n');

  return [...names].sort();
}
