import { DEFAULT_LANG, LANG_STORAGE_KEY, isLang, matchLang, resolveInitialLang } from './lang';

describe('lang', () => {
  // Закреплённый в `src/test-setup.ts` язык здесь мешает: именно его выбор
  // эти проверки и разбирают
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  describe('matchLang', () => {
    it('should match by prefix before the hyphen', () => {
      expect(matchLang(['en-GB'])).toBe('en');
      expect(matchLang(['ru-RU'])).toBe('ru');
    });

    it('should take the first supported language', () => {
      // Порядок в navigator.languages — это порядок предпочтения пользователя
      expect(matchLang(['de', 'fr-FR', 'en-US', 'ru'])).toBe('en');
    });

    it('should be case-insensitive', () => {
      expect(matchLang(['EN-gb'])).toBe('en');
    });

    it('should be null when nothing is supported', () => {
      expect(matchLang(['de', 'fr'])).toBeNull();
    });

    it('should be null on empty input', () => {
      expect(matchLang([])).toBeNull();
      expect(matchLang(undefined)).toBeNull();
    });
  });

  describe('isLang', () => {
    it('should accept supported codes only', () => {
      expect(isLang('ru')).toBe(true);
      expect(isLang('en')).toBe(true);
      expect(isLang('de')).toBe(false);
      expect(isLang('en-GB')).toBe(false);
      expect(isLang(null)).toBe(false);
    });
  });

  describe('resolveInitialLang', () => {
    it('should prefer the stored choice', () => {
      localStorage.setItem(LANG_STORAGE_KEY, 'en');

      expect(resolveInitialLang()).toBe('en');
    });

    it('should take the browser language when there is no stored choice', () => {
      vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-GB', 'ru']);

      expect(resolveInitialLang()).toBe('en');
    });

    it('should ignore an unsupported stored code', () => {
      localStorage.setItem(LANG_STORAGE_KEY, 'de');
      vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-US']);

      expect(resolveInitialLang()).toBe('en');
    });

    it('should fall back to the default when nothing matches', () => {
      vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['de', 'fr']);

      expect(resolveInitialLang()).toBe(DEFAULT_LANG);
    });
  });
});
