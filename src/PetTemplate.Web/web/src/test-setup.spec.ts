import { LANG_STORAGE_KEY } from './app/shared/i18n/lang';
import { SESSION_USER_KEY } from './app/shared/core/user.service';
import { CURRENT_WORKSPACE_KEY } from './app/shared/core/workspace-context.service';

/**
 * Язык интерфейса в тестах — русский, и задаётся он явно.
 *
 * Без этого его выбирает `navigator.languages` браузера, на котором тесты идут:
 * в ChromeHeadless это `en-US`, на машине разработчика — что угодно, и любая
 * проверка подписи («Сохранить», «Сегодня») давала бы разный результат в CI
 * и локально. Проверки перевода на другой язык переключают его сами
 * (`await i18n.setLang('en')`).
 *
 * Обработчик верхнего уровня, а не в каждом файле: он регистрируется на корневом
 * наборе Jasmine и отрабатывает перед `beforeEach` любого `describe`.
 *
 * Файл назван `*.spec.ts` намеренно: сборщик тестов Angular подхватывает только
 * такие, а отдельной точки входа (`test.ts`) у `@angular/build:karma` нет.
 */
beforeEach(() => {
  localStorage.setItem(LANG_STORAGE_KEY, 'ru');

  // Сессия переживает перезагрузку (`UserService`), а значит и соседний тест: вход,
  // оставленный одной спекой, другая получила бы при создании `UserService`
  localStorage.removeItem('token');
  localStorage.removeItem(SESSION_USER_KEY);
  localStorage.removeItem(CURRENT_WORKSPACE_KEY);
});

describe('test setup', () => {
  it('should pin the interface language', () => {
    expect(localStorage.getItem(LANG_STORAGE_KEY)).toBe('ru');
  });
});
