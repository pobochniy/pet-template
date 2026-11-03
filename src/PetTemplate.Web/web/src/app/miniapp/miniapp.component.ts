import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ExternalLoginApiService } from '../shared/api/external-login-api.service';
import { AlertsService } from '../shared/alerts/alerts.service';
import { UserService } from '../shared/core/user.service';
import { MergeStateService } from '../shared/core/merge-state.service';
import { LanguageService } from '../shared/i18n/language.service';
import { createLoginForm } from '../shared/form-models/login-form.model';
import { invitationOf, launchInvitation, miniAppProof } from './mini-app.util';
import { launchParam } from '../shared/core/launch-params';
import { serverErrorMessage } from '../shared/core/server-error.util';
import { AuthenticationResultModel } from '../shared/models/authentication-result.model';
import { ExternalProofModel, ExternalProvider } from '../shared/models/external-login.model';
import { injectI18n } from '../shared/i18n/inject-i18n';
import { MINIAPP_SCOPES } from './miniapp.scopes';

/**
 * Вход из mini-app (спец. accounts, 5.6): `/miniapp` — Telegram, `/vk` — VK.
 * Площадку называет маршрут (`data.provider`), вход — подписанные параметры запуска,
 * которые снял скрипт `index.html` (`launch-params.ts`); SDK площадок не грузятся.
 *
 * Неизвестный вход без приглашения — развилка, а не молчаливое заведение аккаунта:
 * «Начать» заводит аккаунт тем же подписанным входом и ведёт на `/welcome`; «У меня уже
 * есть аккаунт» — вход логином и паролем, и этот Telegram (VK) привязывается к нему.
 * Открыли по приглашению (`start_param=inv_…` у Telegram, `#inv_…` у VK) — после входа
 * человек на лендинге приглашения: там он его принимает
 */
@Component({
  selector: 'app-miniapp',
  templateUrl: './miniapp.component.html',
  styleUrl: './miniapp.component.css',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MiniappComponent implements OnInit {
  protected readonly i18n = injectI18n(...MINIAPP_SCOPES);
  private readonly language = inject(LanguageService);
  private readonly provider: ExternalProvider = inject(ActivatedRoute).snapshot.data['provider'] ?? 'telegram';

  /** Название площадки в текстах экрана */
  protected readonly platform = this.provider === 'vk' ? 'VK' : 'Telegram';

  readonly isLoading = signal(true);
  readonly authError = signal(false);

  /**
   * Развилка первого запуска (5.6): этот вход сервер не знает, а приглашения нет.
   * Аккаунт молча не заводится — человек выбирает сам
   */
  readonly fork = signal<'choose' | 'login' | null>(null);
  readonly busy = signal(false);
  readonly loginForm = createLoginForm();
  private proof: ExternalProofModel | undefined;

  constructor(
    private api: ExternalLoginApiService,
    private userService: UserService,
    private router: Router,
    private alertsService: AlertsService,
    private mergeState: MergeStateService
  ) {}

  async ngOnInit(): Promise<void> {
    try {
      const proof = miniAppProof(this.provider);
      if (!proof) {
        this.alertsService.push('danger', this.i18n.t('miniapp.error.connectFailed', { platform: this.platform }));
        return;
      }

      this.proof = proof;
      let session: AuthenticationResultModel;
      try {
        session = proof.vkLaunchParams
          ? await this.api.vkAuth(proof.vkLaunchParams, invitationOf(launchParam('vkHash')))
          : await this.api.telegramAuth(proof.initData ?? '');
      } catch (error: unknown) {
        const refusal = error as { status?: number; error?: { code?: string } } | null;
        if (refusal?.status === 404 && refusal.error?.code === 'auth.error.unknownLogin') {
          this.fork.set('choose');
          return;
        }
        throw error;
      }

      await this.signIn(session);
      await this.router.navigateByUrl(this.target('/'));
    } catch (error) {
      console.error('Mini-app authentication error:', error);
      this.alertsService.push('danger', this.i18n.t('miniapp.error.connectFailed', { platform: this.platform }));
      this.authError.set(true);
    } finally {
      this.isLoading.set(false);
    }
  }

  /** «Начать» — завести аккаунт этим же входом и перейти на «Добро пожаловать» (5.4) */
  async start(): Promise<void> {
    await this.finish(async () => {
      await this.signIn(await this.api.registerExternal(this.requireProof()));
      return '/welcome';
    });
  }

  /**
   * «У меня уже есть аккаунт» — войти логином и паролем и привязать этот Telegram (VK).
   * Вход успел достаться другому аккаунту — сервер вместо привязки отдаёт просмотр
   * слияния (спец. accounts, раздел 3, 7.1), и человек попадает на экран слияния
   */
  async logInAndLink(): Promise<void> {
    this.loginForm.markAllAsTouched();
    if (this.loginForm.invalid) return;

    const { login, password } = this.loginForm.getRawValue();
    await this.finish(async () => {
      const session = await this.api.logInAndLink(login ?? '', password ?? '', this.requireProof());
      await this.signIn(session);
      if (!session.mergePreview) return this.target('/');

      this.mergeState.open(session.mergePreview);
      return '/profile/merge';
    }, true);
  }

  private requireProof(): ExternalProofModel {
    // Развилка показывается только после того, как доказательство снято в `ngOnInit`
    if (!this.proof) throw new Error('Mini-app launch params are missing');
    return this.proof;
  }

  /** Сессия и язык профиля — выбор, сделанный на другом устройстве (8.4) */
  private async signIn(session: AuthenticationResultModel): Promise<void> {
    this.userService.applySession(session);
    await this.language.adopt(session.profile?.language);
  }

  /** Открыли по приглашению — на его лендинг, иначе — на `fallback` */
  private target(fallback: string): string {
    const invitation = this.proof ? launchInvitation(this.proof) : null;
    return invitation ? `/invite/${invitation}` : fallback;
  }

  /**
   * Текст отказа развилки. Неверная пара логин–пароль (`422`) — своя фраза, как на обычном
   * входе. Остальные отказы («другой вход этой площадки уже привязан», «вход не подтвердился») — по
   * коду сервера: общая фраза здесь выдавала бы понятный отказ за поломку
   */
  private finishError(error: unknown, isLogin: boolean): string {
    if (isLogin && (error as { status?: number } | null)?.status === 422) {
      return this.i18n.t('miniapp.error.invalidCredentials');
    }

    return serverErrorMessage(error, this.i18n, this.i18n.t('miniapp.error.connectFailed', { platform: this.platform }));
  }

  /** `action` называет, куда идти после входа */
  private async finish(action: () => Promise<string>, isLogin = false): Promise<void> {
    this.busy.set(true);
    try {
      await this.router.navigateByUrl(await action());
    } catch (error: unknown) {
      this.alertsService.push('danger', this.finishError(error, isLogin), 5000);
    } finally {
      this.busy.set(false);
    }
  }
}
