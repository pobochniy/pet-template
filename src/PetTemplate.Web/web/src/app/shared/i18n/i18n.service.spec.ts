import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { environment } from '../../../environments/environment';
import { Dict } from './dict';
import { I18nService } from './i18n.service';
import { LOADERS } from './generated';
import { LANG_STORAGE_KEY } from './lang';

describe('I18nService', () => {
  let i18n: I18nService;

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(LANG_STORAGE_KEY, 'ru');

    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    i18n = TestBed.inject(I18nService);
  });

  afterEach(() => localStorage.clear());

  it('should take the starting language from localStorage', () => {
    expect(i18n.lang()).toBe('ru');
  });

  describe('t', () => {
    it('should return the string from the dictionary', () => {
      expect(i18n.t('core.action.save')).toBe('Сохранить');
    });

    it('should substitute parameters', () => {
      expect(i18n.t('core.validation.required', { field: 'Пароль' })).toBe('Поле «Пароль» обязательно');
    });

    it('should keep a placeholder without a value', () => {
      expect(i18n.t('core.validation.required')).toContain('{field}');
    });
  });

  describe('plural', () => {
    it('should cover every Russian category', () => {
      expect(i18n.plural('core.validation.minLength', 1)).toBe('Минимум 1 символ');
      expect(i18n.plural('core.validation.minLength', 3)).toBe('Минимум 3 символа');
      expect(i18n.plural('core.validation.minLength', 5)).toBe('Минимум 5 символов');
      expect(i18n.plural('core.validation.minLength', 21)).toBe('Минимум 21 символ');
    });

    it('should cover every English category', async () => {
      await i18n.setLang('en');

      expect(i18n.plural('core.validation.minLength', 1)).toBe('At least 1 character');
      expect(i18n.plural('core.validation.minLength', 5)).toBe('At least 5 characters');
      expect(i18n.plural('core.validation.minLength', 0)).toBe('At least 0 characters');
    });
  });

  describe('setLang', () => {
    it('should switch core without a reload', async () => {
      await i18n.setLang('en');

      expect(i18n.lang()).toBe('en');
      expect(i18n.t('core.action.save')).toBe('Save');
    });

    it('should mirror the choice into localStorage', async () => {
      await i18n.setLang('en');

      expect(localStorage.getItem(LANG_STORAGE_KEY)).toBe('en');
    });

    it('should do nothing on the current language', async () => {
      await i18n.setLang('ru');

      expect(i18n.t('core.action.save')).toBe('Сохранить');
    });

    it('should survive a screen dictionary that did not arrive', async () => {
      await i18n.loadScope('auth');

      const loader = spyOn(LOADERS.en, 'auth').and.returnValue(Promise.reject(new Error('chunk 404')));

      // Отказ наружу не летит: иначе переключатель языка не закрыл бы окно
      await expectAsync(i18n.setLang('en')).toBeResolved();

      expect(i18n.lang()).toBe('en');
      expect(i18n.t('core.action.save')).toBe('Save');

      // И язык не залипает: словарь экрана помечен старым, следующий заход его перезапросит
      loader.and.callThrough();
      await i18n.loadScope('auth');

      expect(i18n.translateCode('auth.error.invalidCredentials')).toBe('Wrong username or password');
    });
  });

  describe('loadScope', () => {
    it('should make the screen dictionary readable', async () => {
      await i18n.loadScope('auth');

      expect(i18n.translateCode('auth.error.invalidCredentials')).toBe('Неверный логин или пароль');
    });

    it('should reload loaded scopes on a language switch', async () => {
      await i18n.loadScope('auth');
      await i18n.setLang('en');

      expect(i18n.translateCode('auth.error.invalidCredentials')).toBe('Wrong username or password');
    });

    it('should not hand out the previous language while setLang is loading', async () => {
      await i18n.loadScope('auth');

      let arrive!: () => void;
      const chunk = new Promise<{ default: Dict }>(resolve => {
        arrive = () => resolve({ default: { 'auth.error.invalidCredentials': 'Wrong username or password' } });
      });
      spyOn(LOADERS.en, 'auth').and.returnValue(chunk);

      const switching = i18n.setLang('en');

      let loaded = false;
      const loading = i18n.loadScope('auth').then(() => (loaded = true));
      await new Promise(resolve => setTimeout(resolve));

      expect(loaded).withContext('словарь экрана ещё на прошлом языке').toBeFalse();

      arrive();
      await Promise.all([switching, loading]);

      expect(i18n.translateCode('auth.error.invalidCredentials')).toBe('Wrong username or password');
    });
  });

  describe('translateCode', () => {
    it('should substitute the parameters that came with the code', () => {
      expect(i18n.translateCode('core.validation.stringLength', { min: 2, max: 20 }))
        .toBe('От 2 до 20 символов');
    });

    it('should be null for a code the dictionary does not know', () => {
      // Сервер вправе знать код, которого в словаре ещё нет: тогда показывается
      // присланный им текст, а не «⟦…⟧»
      expect(i18n.translateCode('notes.error.somethingBrandNew')).toBeNull();
    });

    it('should be null for a scope that is not loaded', () => {
      expect(i18n.translateCode('auth.error.invalidCredentials')).toBeNull();
    });
  });

  describe('missing key', () => {
    const production = environment.production;
    afterEach(() => (environment.production = production));

    it('should show the key in brackets in development', () => {
      environment.production = false;

      expect(i18n.t('core.action.noSuchKey' as never)).toBe('⟦core.action.noSuchKey⟧');
    });

    it('should show the last segment in production', () => {
      environment.production = true;

      expect(i18n.t('core.action.noSuchKey' as never)).toBe('noSuchKey');
    });
  });
});
