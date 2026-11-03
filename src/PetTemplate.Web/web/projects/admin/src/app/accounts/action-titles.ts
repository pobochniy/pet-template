/**
 * Подписи журнала по `AccountAdminActionKind` — одни на карточку аккаунта и на журнал
 * аккаунтов (спец. admin, 12)
 */
const ACTION_TITLES: Record<number, string> = {
  1: 'ссылка сброса',
  2: 'удаление',
  3: 'освобождение почты',
  4: 'почту сняли для другого аккаунта',
  5: 'освобождение телефона',
  6: 'телефон сняли для другого аккаунта'
};

export function actionTitle(kind: number): string {
  return ACTION_TITLES[kind] ?? `действие ${kind}`;
}
