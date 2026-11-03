import { LANG_STORAGE_KEY } from './app/shared/i18n/lang';

// Сам `beforeEach` — в `src/test-setup.ts` (`setupFiles` в `angular.json`)
describe('test setup', () => {
  it('should pin the interface language', () => {
    expect(localStorage.getItem(LANG_STORAGE_KEY)).toBe('ru');
  });
});
