import {
  ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, output, signal, viewChild
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { AccountApiService } from '../../shared/api/account-api.service';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { UserService } from '../../shared/core/user.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { AccountLoginsModel } from '../../shared/models/account.model';
import { ExternalProvider } from '../../shared/models/external-login.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { FreshLoginService } from './fresh-login.service';

/**
 * «Подтвердите, что это вы» в блоке «Основное» (спец. accounts, 8.6): появляется, когда
 * привязка, отвязка, смена почты или слияние ждут подтверждения (`FreshLoginService`).
 * Аккаунт с паролем подтверждает паролем — ответ сервера новая сессия, и действие идёт
 * дальше само. Без пароля — только заново войти через привязанную площадку: уход
 * со страницы, действие человек повторит, вернувшись
 */
@Component({
  selector: 'profile-fresh-login',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (fresh.pending()) {
      <div class="alert alert-warning rounded-0 mb-0" id="fresh-login" role="alert">
        <div class="fw-semibold mb-1">{{ i18n.t('profile.fresh.title') }}</div>
        @if (logins().hasPassword) {
          <div class="small mb-2">{{ i18n.t('profile.fresh.passwordHint') }}</div>
          <form class="d-flex flex-wrap gap-2" [formGroup]="form" (ngSubmit)="confirm()">
            <input #passwordInput class="form-control form-control-sm w-auto flex-grow-1" id="fresh-password"
                   type="password" autocomplete="current-password" [formControl]="password"
                   [class.is-invalid]="password.touched && password.invalid"
                   [attr.aria-label]="i18n.t('core.field.password')" [placeholder]="i18n.t('core.field.password')" />
            <button type="submit" class="btn btn-sm btn-primary" id="fresh-confirm" [disabled]="busy()">
              {{ i18n.t('profile.fresh.confirm') }}
            </button>
            <button type="button" class="btn btn-sm btn-outline-secondary" id="fresh-cancel" (click)="cancel()">
              {{ i18n.t('core.action.cancel') }}
            </button>
          </form>
        } @else {
          <div class="small mb-2">{{ i18n.t('profile.fresh.reloginHint') }}</div>
          <div class="d-flex flex-wrap gap-2">
            @for (provider of relogins(); track provider) {
              <button type="button" class="btn btn-sm btn-primary" [id]="'fresh-' + provider"
                      [disabled]="busy()" (click)="relogin(provider)">
                {{ i18n.t(provider === 'telegram' ? 'profile.fresh.telegram' : 'profile.fresh.vk') }}
              </button>
            }
            <button type="button" class="btn btn-sm btn-outline-secondary" id="fresh-cancel" (click)="cancel()">
              {{ i18n.t('core.action.cancel') }}
            </button>
          </div>
        }
        @if (error()) {
          <div class="text-danger small mt-1" id="fresh-error">{{ error() }}</div>
        }
      </div>
    }
  `
})
export class ProfileFreshLoginComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly fresh = inject(FreshLoginService);
  private readonly workspaceContext = inject(WorkspaceContextService);

  readonly logins = input.required<AccountLoginsModel>();

  /** Площадки, настроенные на сервере: войти заново можно только через них */
  readonly enabled = input<Record<ExternalProvider, boolean>>({ telegram: false, vk: false });

  /** Подтвердили паролем — новые «Основное» для родителя */
  readonly changed = output<AccountLoginsModel>();

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly password = new FormControl('', { nonNullable: true, validators: Validators.required });

  /** Форма — ради `ngSubmit`: Enter в поле пароля подтверждает, а не перезагружает страницу */
  protected readonly form = new FormGroup({ password: this.password });

  /** Привязанные и настроенные площадки — ими и входить заново */
  protected readonly relogins = computed(() => {
    const logins = this.logins();
    const enabled = this.enabled();
    return (['telegram', 'vk'] as const).filter(provider =>
      enabled[provider] && (provider === 'telegram' ? logins.telegramLinked : logins.vkLinked));
  });

  private readonly passwordInput = viewChild<ElementRef<HTMLInputElement>>('passwordInput');

  constructor(
    private account: AccountApiService,
    private external: ExternalLoginService,
    private userService: UserService
  ) {
    // Запрос мог прийти от кнопки внизу блока — показать его там, где он появился
    effect(() => {
      const input = this.passwordInput()?.nativeElement;
      input?.scrollIntoView({ block: 'center' });
      input?.focus();
    });
    effect(() => {
      if (!this.fresh.pending()) this.reset();
    });
  }

  protected async confirm(): Promise<void> {
    this.password.markAsTouched();
    if (this.password.invalid || this.busy()) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      this.userService.applySession(
        await this.account.confirmPassword(this.password.value), this.workspaceContext.getCurrentWorkspaceId());
      const logins = await this.account.logins();
      this.changed.emit(logins);
      this.fresh.confirmed(logins);
    } catch (e: unknown) {
      this.error.set((e as { status?: number } | null)?.status === 422
        ? this.i18n.t('auth.error.wrongPassword')
        : serverErrorMessage(e, this.i18n, this.i18n.t('profile.fresh.failed')));
    } finally {
      this.busy.set(false);
    }
  }

  protected async relogin(provider: ExternalProvider): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      // Вернётся человек сюда же со свежим входом: действие он повторит сам
      await this.external.start(provider, 'login', '/profile/edit');
      this.fresh.cancel();
    } catch (e) {
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.fresh.failed')));
    } finally {
      this.busy.set(false);
    }
  }

  protected cancel(): void {
    this.fresh.cancel();
  }

  private reset(): void {
    this.password.reset();
    this.error.set(null);
  }
}
