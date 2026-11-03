import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { InvitationApiService } from '../shared/api/invitation-api.service';
import { AuthApiService } from '../shared/api/auth-api.service';
import { UserService } from '../shared/core/user.service';
import { AlertsService } from '../shared/alerts/alerts.service';
import { InvitationStatusEnum } from '../shared/enums/invitation-status.enum';
import { InvitationPreviewModel } from '../shared/models/workspace-invitation.model';
import { createInviteRegisterForm } from './form-models/invite-register-form.model';
import { createLoginForm } from '../shared/form-models/login-form.model';
import { FormValidationComponent } from '../shared/form-validation/form-validation.component';
import { INVITATION_UNAVAILABLE_KEY, invitationRefusalKey } from './invitation-messages';
import { serverErrorMessage } from '../shared/core/server-error.util';
import { applyServerValidationErrors } from '../shared/core/server-validation.util';
import { ExternalButtonsComponent } from '../auth/external-buttons/external-buttons.component';
import { injectI18n } from '../shared/i18n/inject-i18n';
import { INVITE_SCOPES } from './invite.scopes';

/**
 * Лендинг приглашения `/invite/:token` (спец. authorization, 6.3). Анонимный.
 *
 * Две явные дорожки, а не одна форма «на все случаи»: по реакции общей формы
 * на пару логин+пароль можно было бы отличить «имя занято» от «пароль неверный»,
 * и текст ошибки стало бы невозможно написать так, чтобы он и не путал своего,
 * и не помогал чужому.
 *
 * Под обеими формами — «Войти через Telegram» и «Войти через VK» (спец. accounts, 5.1):
 * вход вернёт сюда же, и лендинг предложит принять приглашение (5.4)
 */
@Component({
  selector: 'app-invite',
  templateUrl: './invite.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, FormValidationComponent, RouterLink, ExternalButtonsComponent]
})
export class InviteComponent {
  protected readonly i18n = injectI18n(...INVITE_SCOPES);
  private readonly invitationApi = inject(InvitationApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly userService = inject(UserService);
  private readonly alerts = inject(AlertsService);
  private readonly router = inject(Router);

  /** Токен приезжает из адреса через `withComponentInputBinding()` */
  readonly token = input.required<string>();

  /** Вход через площадку возвращает сюда же — лендинг предложит принять приглашение (5.4) */
  protected readonly returnUrl = computed(() => `/invite/${this.token()}`);

  readonly preview = signal<InvitationPreviewModel | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);

  /** Проверить ссылку не удалось: сервер не ответил, а не отказал по существу */
  readonly unavailable = signal(false);
  readonly unavailableKey = INVITATION_UNAVAILABLE_KEY;

  /** Показываем форму входа вместо регистрации — по ссылке «У меня уже есть аккаунт» */
  readonly hasAccount = signal(false);

  readonly registerForm = createInviteRegisterForm();
  readonly loginForm = createLoginForm();

  readonly refusal = computed(() => {
    const preview = this.preview();

    return preview && preview.status !== InvitationStatusEnum.Active ? invitationRefusalKey(preview.status) : null;
  });

  constructor() {
    // Загрузку запускает вход компонента, а не обработчик: токен приезжает адресом
    effect(() => {
      void this.loadPreview(this.token());
    });
  }

  get isAuth(): boolean {
    return this.userService.isAuthenticated;
  }

  /** Вошедшему показываем одну кнопку: логин и пароль у него уже есть */
  async accept() {
    await this.run(async () => this.userService.applyAcceptedInvitation(await this.invitationApi.accept(this.token())));
  }

  async register() {
    this.registerForm.markAllAsTouched();
    if (!this.registerForm.valid) return;

    const value = this.registerForm.getRawValue();
    await this.run(async () => this.userService.applyAcceptedInvitation(
      await this.invitationApi.register(this.token(), value.userName.trim(), value.password, value.passwordConfirm, this.i18n.lang())));
  }

  /**
   * Свой аккаунт: сначала вход, потом принятие. Вход в пространство не добавляет —
   * это делает только само приглашение
   */
  async login() {
    this.loginForm.markAllAsTouched();
    if (!this.loginForm.valid) return;

    await this.run(async () => {
      this.userService.applySession(await this.authApi.login(this.loginForm));
      this.userService.applyAcceptedInvitation(await this.invitationApi.accept(this.token()));
    });
  }

  /** Повтор проверки после сбоя связи — руками, кнопкой с плашки */
  async retry() {
    await this.loadPreview(this.token());
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    try {
      await action();
      await this.router.navigateByUrl('/');
    } catch (error: unknown) {
      // Правила логина и пароля — 422 у поля формы регистрации (спец. accounts, 4.1 и 8.1):
      // встают под поля, а не плашкой, и состояние ссылки перечитывать незачем
      if ((error as { status?: number } | null)?.status === 422) {
        applyServerValidationErrors(this.registerForm, error);
        return;
      }

      // Отказ по существу сервер присылает кодом и текстом — переводим код
      this.alerts.push('danger', serverErrorMessage(error, this.i18n, this.i18n.t('invite.page.acceptFailed')), 5000);
      // Ссылка могла умереть, пока страница была открыта, — перечитываем состояние
      await this.loadPreview(this.token());
    } finally {
      this.busy.set(false);
    }
  }

  private async loadPreview(token: string) {
    this.loading.set(true);
    this.unavailable.set(false);
    try {
      this.preview.set(await this.invitationApi.preview(token));
    } catch (error: unknown) {
      if (isBrokenLink(error)) {
        this.preview.set({ status: InvitationStatusEnum.Expired, workspaceName: null, inviterName: null, inviteeName: null });
      } else {
        // Сервер не ответил — просить новую ссылку не за что, она вела бы туда же
        this.preview.set(null);
        this.unavailable.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }
}

/**
 * Мёртвую ссылку `Preview` отдаёт статусом в теле обычного 200-го ответа — 404 там нет
 * намеренно, иначе эндпоинт подтверждал бы существование токенов. Значит из ошибок
 * про ссылку говорит только отказ разобрать сам токен (не `Guid` в адресе — 400),
 * а 5xx, ответы прокси и обрыв связи (`status === 0`) — про сервер
 */
function isBrokenLink(error: unknown): boolean {
  const status = (error as { status?: number } | null)?.status;

  return status === 400 || status === 422;
}
