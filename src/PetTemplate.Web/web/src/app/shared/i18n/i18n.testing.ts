import { I18nService } from './i18n.service';
import { LOADERS } from './generated';
import { LazyScope } from './generated/i18n-keys';

/**
 * Загрузка всех словарей экранов — для тестов.
 *
 * В приложении словарь экрана приезжает вместе с его чанком: это делает
 * `loadScope` в `loadComponent` маршрута. В юнит-тесте маршрута нет, компонент
 * создаётся напрямую, и без этой строки подписи возвращали бы последний сегмент
 * ключа («reporter» вместо «постановщик»).
 *
 * Грузим все словари, а не один: спека проверяет экран целиком, а составлять
 * список его словарей руками — лишний повод разойтись.
 */
export async function loadAllScopes(i18n: I18nService): Promise<void> {
  const scopes = Object.keys(LOADERS['en']) as LazyScope[];

  await Promise.all(scopes.map(scope => i18n.loadScope(scope)));
}
