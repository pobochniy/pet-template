import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountLoginsModel } from '../../shared/models/account.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';

/**
 * Состояние почты под полем «Почта» в блоке «Основное» (спец. accounts, 6 и 8.4):
 * подтверждена ли сохранённая почта и «Отправить ещё раз». Сам адрес правится полем
 * формы — новая почта сразу получает письмо со ссылкой. Пока в поле не тот адрес,
 * что сохранён, состояние сохранённого не показывается: оно было бы не про то, что видно.
 * Подтверждённая почта — второй путь восстановления пароля (8.2)
 */
@Component({
  selector: 'profile-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isSaved()) {
      @if (logins().emailConfirmed) {
        <div class="small text-success mt-1" id="email-confirmed">{{ i18n.t('profile.email.confirmed') }}</div>
      } @else {
        <div class="small mt-1" id="email-unconfirmed">
          <span class="text-body-secondary">{{ i18n.t('profile.email.unconfirmed') }}</span> ·
          <button type="button" class="btn btn-link btn-sm p-0 align-baseline" id="email-resend"
                  [disabled]="busy()" (click)="resend()">
            {{ i18n.t('profile.email.resend') }}
          </button>
        </div>
      }
    }

    @if (notice()) {
      <div class="small text-success mt-1" id="email-notice">{{ notice() }}</div>
    }
    @if (error()) {
      <div class="small text-danger mt-1" id="email-error">{{ error() }}</div>
    }
  `
})
export class ProfileEmailComponent {
  protected readonly i18n = inject(I18nService);

  readonly logins = input.required<AccountLoginsModel>();

  /** Что сейчас в поле «Почта» */
  readonly value = input('');

  /** В поле — сохранённый адрес: про него и можно сказать, подтверждён ли он */
  protected readonly isSaved = computed(() => {
    const saved = this.logins().email;
    return !!saved && this.value().trim().toLowerCase() === saved.toLowerCase();
  });

  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor(private account: AccountApiService) {}

  protected async resend(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await this.account.sendEmailConfirmation();
      this.notice.set(this.i18n.t('profile.email.sent'));
    } catch (e) {
      // Лимит писем (`auth.error.tooManyEmails`) — текстом сервера в переводе
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.email.failed')));
    } finally {
      this.busy.set(false);
    }
  }
}
