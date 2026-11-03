import { ExternalProviderEnum } from '../shared/enums/external-provider.enum';
import { ExternalProofModel, ExternalProvider } from '../shared/models/external-login.model';
import { launchParam } from '../shared/core/launch-params';

/**
 * Подписанный `initData` mini-app Telegram — если приложение открыто внутри Telegram.
 * Снаружи — `undefined`.
 *
 * Telegram кладёт подпись во фрагмент (`#tgWebAppData=…`), а роутер его выкидывает, —
 * поэтому её снимает скрипт `index.html` до старта Angular. SDK Telegram не подключается:
 * подписи из адреса входу достаточно, а SDK — лишние килобайты
 */
export function telegramInitData(): string | undefined {
  const fromWebApp = (window as { Telegram?: { WebApp?: { initData?: string } } }).Telegram?.WebApp?.initData;
  return fromWebApp || launchParam('tg');
}

/**
 * Подписанные параметры запуска mini-app VK — строка запроса, с которой VK открыл `/vk`
 * (`vk_user_id=…&sign=…`), как есть: подпись сервер проверяет по ней. Не в VK — `undefined`
 */
export function vkLaunchParams(): string | undefined {
  return launchParam('vk');
}

/**
 * Доказательство входа площадки изнутри её mini-app — подписанные параметры запуска.
 * Снаружи mini-app — `undefined`
 */
export function miniAppProof(provider: ExternalProvider): ExternalProofModel | undefined {
  if (provider === 'telegram') {
    const initData = telegramInitData();
    return initData ? { provider: ExternalProviderEnum.Telegram, initData } : undefined;
  }

  const vkLaunch = vkLaunchParams();
  return vkLaunch ? { provider: ExternalProviderEnum.Vk, vkLaunchParams: vkLaunch } : undefined;
}

/** Параметр приглашения в запуске mini-app: `inv_<токен>` */
const INVITATION_PREFIX = 'inv_';

/**
 * Токен приглашения из параметра запуска: `start_param` Telegram (он внутри подписанного
 * `initData`) или фрагмент VK. Токен — `Guid`; без дефисов (`startapp` их не пропускает)
 * дефисы возвращаются. Не приглашение — `null`
 */
export function invitationOf(param: string | null | undefined): string | null {
  if (!param?.startsWith(INVITATION_PREFIX)) return null;

  const raw = param.slice(INVITATION_PREFIX.length).toLowerCase();
  if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(raw)) return raw;
  if (!/^[0-9a-f]{32}$/.test(raw)) return null;

  return `${raw.slice(0, 8)}-${raw.slice(8, 12)}-${raw.slice(12, 16)}-${raw.slice(16, 20)}-${raw.slice(20)}`;
}

/** Приглашение, с которым открыли mini-app: Telegram — `start_param` из `initData`, VK — фрагмент */
export function launchInvitation(proof: ExternalProofModel): string | null {
  if (proof.initData) return invitationOf(new URLSearchParams(proof.initData).get('start_param'));
  return invitationOf(launchParam('vkHash'));
}
