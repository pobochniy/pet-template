import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { ExternalLoginApiService } from '../api/external-login-api.service';
import { ExternalLoginsConfigModel, ExternalProvider, ExternalPurpose } from '../models/external-login.model';
import { safeStorage } from './safe-storage';

/** Ключ `sessionStorage`: живёт ровно до возврата с площадки и только в этой вкладке */
export const EXTERNAL_LOGIN_STORAGE_KEY = 'pettemplate.externalLogin';

/**
 * Что запомнили перед уходом на площадку — `state`, PKCE-verifier (только VK) и куда вернуться.
 * Telegram сюда же кладёт `id_token` из окна входа — или `null`, если Telegram отказал
 */
export interface PendingExternalLogin {
  provider: ExternalProvider;
  purpose: ExternalPurpose;
  state: string;
  verifier: string;
  returnUrl: string;
  idToken?: string | null;
}

/** Окно входа Telegram: размеры — как у его собственной библиотеки `telegram-login.js` */
const TELEGRAM_POPUP_FEATURES = 'width=550,height=650,status=0,location=0,menubar=0,toolbar=0';

/**
 * Вход через Telegram и VK ID на сайте (спец. accounts, 5). Один каркас на обе площадки
 * и на все назначения: вход, привязку в профиле (раздел 6) и восстановление доступа (8.3).
 * SDK площадок не подключаются: окно Telegram и адрес VK ID собираются здесь же.
 *
 * Telegram — «Log In with Telegram» во всплывающем окне: окно присылает странице
 * `id_token` сообщением (`postMessage`), и сервер проверяет его сам, без запросов
 * в Telegram. `state` уходит окну как `nonce` и возвращается внутри подписанного токена.
 * VK ID — переход во всё окно, код и PKCE.
 *
 * Перед уходом на площадку `state`, `verifier` и адрес «откуда начинали» ложатся
 * в `sessionStorage`: в памяти они не пережили бы перезагрузку страницы возвратом (5.4).
 * Страница возврата (`/auth/telegram`, `/auth/vk`) забирает их ровно один раз — `take` —
 * и сверяет `state`: чужой возврат (подделанная ссылка) не совпадёт. Telegram на неё
 * не возвращает — туда ведёт сама страница, получив токен, чтобы вход и привязка шли
 * одним путём для обеих площадок
 */
@Injectable({ providedIn: 'root' })
export class ExternalLoginService {
  constructor(private api: ExternalLoginApiService, private router: Router) {}

  /** Включённые на сервере площадки: пустой id — кнопки нет */
  async enabled(): Promise<Record<ExternalProvider, boolean>> {
    try {
      const config = await this.api.externalLogins();
      return { telegram: !!config.telegramBotId, vk: !!config.vkAppId };
    } catch {
      return { telegram: false, vk: false };
    }
  }

  /**
   * Уйти на площадку. `returnUrl` — куда вернуть после входа (в том числе `/invite/:token`).
   * Telegram: промис завершается, когда окно входа закрылось, — с переходом на страницу
   * возврата или без него, если человек закрыл окно сам (5.2).
   *
   * `opened` — окно Telegram, открытое заранее в клике, после которого шли запросы
   * (подтверждение паролем перед привязкой, `FreshLoginPrepare`): открыть его здесь,
   * после ответа сервера, браузер уже не дал бы
   */
  async start(provider: ExternalProvider, purpose: ExternalPurpose, returnUrl: string, opened?: Window): Promise<void> {
    // Окно — до первого `await`: иначе браузер не увидит в нём ответ на клик и заблокирует
    const popup = provider === 'telegram' ? (opened && !opened.closed ? opened : this.openPopup()) : null;
    if (provider === 'telegram' && !popup) throw new Error('Telegram sign-in window was blocked');

    try {
      const config = await this.api.externalLogins();
      const verifier = randomToken();
      const state = randomToken();

      const pending: PendingExternalLogin = { provider, purpose, state, verifier, returnUrl: safeReturnUrl(returnUrl) };
      if (provider === 'vk') {
        safeStorage.setJson(EXTERNAL_LOGIN_STORAGE_KEY, pending, 'session');
        this.redirect(vkAuthorizeUrl(config, state, await challenge(verifier)));
        return;
      }

      // `popup` есть: без окна Telegram вышли выше
      const result = await telegramPopup(popup!, config, state);
      if (result === 'closed') return;

      safeStorage.setJson(EXTERNAL_LOGIN_STORAGE_KEY, { ...pending, idToken: result.idToken }, 'session');
      await this.router.navigateByUrl(`/auth/telegram?state=${encodeURIComponent(state)}`);
    } catch (e) {
      popup?.close();
      throw e;
    }
  }

  /**
   * Забрать запомненное при возврате — один раз. Площадка не та или `state` не совпал —
   * `null`: такой возврат не наш, и код из него на сервер не уходит
   */
  take(provider: ExternalProvider, state: string | null): PendingExternalLogin | null {
    const pending = safeStorage.getJson<PendingExternalLogin>(EXTERNAL_LOGIN_STORAGE_KEY, 'session');
    safeStorage.remove(EXTERNAL_LOGIN_STORAGE_KEY, 'session');

    if (!pending || pending.provider !== provider) return null;
    return state && pending.state === state ? pending : null;
  }

  /** Переход на площадку — отдельным методом, чтобы спека могла его перехватить */
  redirect(url: string): void {
    window.location.assign(url);
  }

  /** Пустое окно под вход Telegram — отдельным методом, чтобы спека могла его подменить */
  openPopup(): Window | null {
    return window.open('', 'telegram_oidc_login', TELEGRAM_POPUP_FEATURES);
  }
}

/**
 * Вход в окне Telegram: окно открывает страницу входа с `response_type=post_message`
 * (так её открывает и `telegram-login.js` Telegram) и по завершении шлёт странице-хозяйке
 * `{ event: 'auth_result', result: <id_token> }` или `{ event: 'auth_result', error }`.
 * Сообщения принимаются только от этого окна и только с адреса страницы входа.
 * Отказ Telegram — `idToken: null`: страница возврата скажет, что вход отменён.
 * Окно закрыли без ответа — `'closed'`
 */
function telegramPopup(popup: Window, config: ExternalLoginsConfigModel, state: string): Promise<{ idToken: string | null } | 'closed'> {
  const endpoint = config.telegramAuthorizationEndpoint;
  const origin = new URL(endpoint).origin;
  const params = new URLSearchParams({
    response_type: 'post_message',
    client_id: config.telegramBotId,
    redirect_uri: config.telegramRedirectUri,
    scope: 'openid profile',
    nonce: state
  });

  return new Promise(resolve => {
    let timer: ReturnType<typeof setInterval> | undefined;
    const finish = (result: { idToken: string | null } | 'closed') => {
      window.removeEventListener('message', onMessage);
      clearInterval(timer);
      resolve(result);
    };

    const onMessage = (event: MessageEvent) => {
      if (event.origin !== origin || event.source !== popup) return;

      let data: { event?: string; result?: unknown; error?: unknown };
      try {
        data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
      } catch {
        return;
      }
      if (data?.event !== 'auth_result') return;

      popup.close();
      finish({ idToken: !data.error && typeof data.result === 'string' ? data.result : null });
    };

    window.addEventListener('message', onMessage);
    timer = setInterval(() => {
      if (popup.closed) finish('closed');
    }, 300);
    popup.location.href = `${endpoint}?${params.toString()}`;
  });
}

function vkAuthorizeUrl(config: ExternalLoginsConfigModel, state: string, codeChallenge: string): string {
  const params = new URLSearchParams({
    client_id: config.vkAppId,
    redirect_uri: config.vkRedirectUri,
    response_type: 'code',
    scope: 'vkid.personal_info',
    state,
    code_challenge: codeChallenge,
    code_challenge_method: 'S256'
  });
  return `${config.vkAuthorizationEndpoint}?${params.toString()}`;
}

/** Только свой адрес: открытый редирект через `returnUrl` уводил бы с сайта */
function safeReturnUrl(url: string): string {
  return url.startsWith('/') && !url.startsWith('//') ? url : '/';
}

/** 32 случайных байта в base64url — и для verifier, и для state */
function randomToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return base64Url(bytes);
}

async function challenge(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  return base64Url(new Uint8Array(digest));
}

function base64Url(bytes: Uint8Array): string {
  let binary = '';
  bytes.forEach(b => binary += String.fromCharCode(b));
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
