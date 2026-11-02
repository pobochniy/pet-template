/**
 * Параметры запуска mini-app, которые снял инлайн-скрипт `index.html` до старта Angular
 * (спец. accounts, 5.6) — как у Lad:
 *
 * - `tg` — подписанный `initData` Telegram из фрагмента `#tgWebAppData=…`;
 * - `vk` — строка запроса, с которой VK открыл `/vk`, как есть: подпись сервер проверяет
 *   по ней, и набор `vk_*` растёт;
 * - `vkHash` — фрагмент адреса VK (`#inv_<токен>` — приглашение).
 *
 * Сначала память страницы — этот запуск, потом `sessionStorage` — после перезагрузки
 * вкладки, когда адрес уже другой
 */
export type LaunchParam = 'tg' | 'vk' | 'vkHash';

/** Префикс ключей в `sessionStorage`; тот же, что пишет скрипт в `index.html` */
export const LAUNCH_STORAGE_PREFIX = 'pettemplateLaunch.';

export function launchParam(name: LaunchParam): string | undefined {
  const fromPage = (window as { __pettemplateLaunch?: Partial<Record<LaunchParam, string>> }).__pettemplateLaunch?.[name];
  if (fromPage) return fromPage;

  try {
    return sessionStorage.getItem(LAUNCH_STORAGE_PREFIX + name) || undefined;
  } catch {
    // Стороннему фрейму VK хранилище могут не дать
    return undefined;
  }
}
