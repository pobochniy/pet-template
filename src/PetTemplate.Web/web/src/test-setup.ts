import './test-env';
import { LANG_STORAGE_KEY } from './app/shared/i18n/lang';
import { SESSION_KEY } from './app/shared/core/session-store';
import { CURRENT_WORKSPACE_KEY } from './app/shared/core/workspace-context.service';

/**
 * Язык интерфейса в тестах — русский, и задаётся он явно.
 *
 * Без этого его выбирает `navigator.languages` среды, в которой тесты идут:
 * в jsdom это `en-US`, и любая проверка подписи («Сохранить», «Сегодня») шла бы
 * по-английски. Проверки перевода на другой язык переключают его сами
 * (`await i18n.setLang('en')`).
 *
 * Файл подключён `setupFiles` в `angular.json`: Vitest гоняет каждый файл спек
 * в своём окружении и выполняет его перед каждым, поэтому `beforeEach` здесь
 * отрабатывает перед `beforeEach` любого `describe` любой спеки. Глобальные
 * функции тестов есть только в `tsconfig.spec.json` — из `tsconfig.app.json` файл
 * исключён. Что закрепление работает, проверяет `test-setup.spec.ts`.
 */
beforeEach(() => {
  localStorage.setItem(LANG_STORAGE_KEY, 'ru');

  // Сессия переживает перезагрузку (`UserService`), а значит и соседний тест: вход,
  // оставленный одним тестом, другой получил бы при создании `UserService`.
  // `token` и `user` — раскладка до общей записи сессии (`session-store.ts`)
  for (const key of [SESSION_KEY, 'token', 'user', CURRENT_WORKSPACE_KEY]) localStorage.removeItem(key);
  sessionStorage.removeItem(CURRENT_WORKSPACE_KEY);
});
