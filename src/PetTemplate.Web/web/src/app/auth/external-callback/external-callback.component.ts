import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { AccountApiService } from '../../shared/api/account-api.service';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { UserService } from '../../shared/core/user.service';
import { MergeStateService } from '../../shared/core/merge-state.service';
import { MergePreviewModel } from '../../shared/models/account.model';
import {
  EXTERNAL_PROVIDER_CODES, ExternalCodeModel, ExternalProofModel, ExternalProvider
} from '../../shared/models/external-login.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { LanguageService } from '../../shared/i18n/language.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';

/** Адрес возврата с одноразовым кодом в истории не остаётся */
const REPLACE = { replaceUrl: true } as const;

/** Экран слияния (спец. accounts, 7.3) — просмотр ему передаёт `MergeStateService`, а не адрес */
const MERGE_URL = '/profile/merge';

/**
 * Куда после входа через площадку (спец. accounts, 5.4). Аккаунт только что заведён —
 * на «Добро пожаловать», кроме входа по приглашению: человека уже ждут в пространстве,
 * и лендинг приглашения сам предложит его принять. Иначе — туда, откуда начинали
 */
export function afterSignIn(isNewAccount: boolean, returnUrl: string): string {
  return isNewAccount && !returnUrl.startsWith('/invite/') ? '/welcome' : returnUrl;
}

/**
 * Возврат с площадки — `/auth/telegram` и `/auth/vk` (спец. accounts, 5.4). Один каркас
 * на обе площадки: площадку называет маршрут (`data.provider`).
 *
 * VK ID возвращает код в `?code=`. Telegram сюда не возвращает: `id_token` из окна входа
 * страница кладёт к запомненному и ведёт сюда сама. Страница сверяет `state` с запомненным
 * перед уходом, отдаёт данные серверу (VK — с PKCE-verifier, Telegram — со `state`,
 * зашитым в токен как `nonce`) и уходит туда, откуда начинали, — в том числе на
 * `/invite/:token`; только что заведённый аккаунт без приглашения — на `/welcome`.
 * Не совпал `state` — на сервер не уходит ничего: такой возврат не наш.
 *
 * Вошедший приходит сюда привязать вход (раздел 6) или доказать владение вторым аккаунтом
 * для слияния (7.1): то же доказательство уходит в `Account/Link` или `Account/MergePreview`.
 * Оба пути, если вход принадлежит другому аккаунту, кончаются экраном слияния `/profile/merge`.
 *
 * Уходит с `replaceUrl`: адрес возврата с кодом одноразовый, и «Назад» на него вёл бы
 * к отказу. При отказе кнопка ведёт туда, откуда пришёл человек: вошедший (привязка,
 * слияние) — в профиль, гость — ко входу
 */
@Component({
  selector: 'auth-external-callback',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container py-5 text-center" id="external-callback">
      @if (error()) {
        <div class="alert alert-danger" id="external-error">{{ error() }}</div>
        @if (signedIn) {
          <a class="btn btn-primary" id="external-back" [routerLink]="['/profile/edit']">{{ i18n.t('auth.external.backToProfile') }}</a>
        } @else {
          <a class="btn btn-primary" id="external-back" [routerLink]="['/auth/login']">{{ i18n.t('auth.external.backToLogin') }}</a>
        }
      } @else {
        <div class="spinner-border text-primary" role="status" aria-hidden="true"></div>
        <div class="mt-3 form-text">{{ i18n.t('auth.external.signingIn') }}</div>
      }
    </div>
  `
})
export class ExternalCallbackComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly language = inject(LanguageService);
  private readonly location = inject(Location);

  protected readonly error = signal<string | null>(null);

  /** Человек уже вошёл — пришёл привязывать вход */
  protected readonly signedIn: boolean;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private external: ExternalLoginService,
    private api: ExternalLoginApiService,
    private account: AccountApiService,
    private userService: UserService,
    private mergeState: MergeStateService
  ) {
    this.signedIn = userService.isAuthenticated;
  }

  async ngOnInit(): Promise<void> {
    const provider = this.route.snapshot.data['provider'] as ExternalProvider;
    const query = this.route.snapshot.queryParamMap;
    // Код и state — из адреса сразу: на ветках отказа ниже перехода с REPLACE нет
    this.location.replaceState(this.location.path().split('?')[0]);
    const pending = this.external.take(provider, query.get('state'));
    const code = provider === 'telegram' ? pending?.idToken ?? null : query.get('code');

    // Площадка вернула отказ вместо кода — человек передумал на её стороне. Отказ
    // Telegram окно входа запоминает как пустой `idToken`
    if (query.get('error') || (provider === 'telegram' && pending && !code)) {
      this.error.set(this.i18n.t('auth.external.cancelled'));
      return;
    }

    if (!pending || !code) {
      this.error.set(this.i18n.t('auth.external.stateMismatch'));
      return;
    }

    const request: ExternalCodeModel = {
      code,
      verifier: provider === 'vk' ? pending.verifier : null,
      deviceId: query.get('device_id'),
      state: pending.state,
      // Восстановление доступа (8.3): только существующий аккаунт, без заведения нового
      existingOnly: pending.purpose === 'recover'
    };

    try {
      if (pending.purpose === 'merge') {
        await this.openMerge(await this.account.mergePreview({ external: proof(provider, request) }));
        return;
      }

      if (pending.purpose === 'link') {
        // Вход чужого аккаунта — привязки нет, сервер отвечает просмотром слияния (7.1):
        // код площадки одноразовый, и второй раз доказать им владение не выйдет
        const result = await this.account.link(proof(provider, request));
        if (result.mergePreview) {
          await this.openMerge(result.mergePreview);
          return;
        }

        await this.router.navigateByUrl(pending.returnUrl, REPLACE);
        return;
      }

      const result = provider === 'telegram'
        ? await this.api.telegramLogin(request)
        : await this.api.vkId(request);

      this.userService.applySession(result);
      // Язык профиля — выбор с другого устройства (8.4); новому аккаунту он и так этот
      await this.language.adopt(result.profile?.language);

      await this.router.navigateByUrl(afterSignIn(result.isNewAccount === true, pending.returnUrl), REPLACE);
    } catch (e) {
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('auth.error.externalFailed')));
    }
  }

  private async openMerge(preview: MergePreviewModel): Promise<void> {
    this.mergeState.open(preview);
    await this.router.navigateByUrl(MERGE_URL, REPLACE);
  }
}

/** Доказательство владения для `Account/Link` и `Account/MergePreview` — то, что вернула площадка */
function proof(provider: ExternalProvider, request: ExternalCodeModel): ExternalProofModel {
  return {
    provider: EXTERNAL_PROVIDER_CODES[provider],
    code: request.code,
    verifier: request.verifier,
    deviceId: request.deviceId,
    state: request.state
  };
}
