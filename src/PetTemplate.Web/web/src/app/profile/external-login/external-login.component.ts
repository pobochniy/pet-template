import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { AccountApiService } from '../../shared/api/account-api.service';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { AccountLoginsModel } from '../../shared/models/account.model';
import { EXTERNAL_PROVIDER_CODES, ExternalProvider } from '../../shared/models/external-login.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { FreshLoginService } from '../fresh-login/fresh-login.service';

/**
 * Строка Telegram или VK в блоке «Основное» (спец. accounts, раздел 6): «Привязан · Отвязать»
 * или «Привязать». Привязка — тем же окном Telegram или переходом на VK ID, что вход,
 * с пометкой «привязка»: вернётся человек на `/auth/{площадка}`, а оттуда — сюда.
 * «Отвязать» у последнего способа войти недоступна — с подсказкой (раздел 3).
 * И то и другое — после подтверждения владения (8.6): привязка — до ухода на площадку,
 * её код одноразовый, и отказ после возврата потратил бы его зря
 */
@Component({
  selector: 'profile-external-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="d-flex flex-wrap align-items-center gap-2">
      <span class="fw-semibold flex-grow-1">{{ i18n.t(titleKey()) }}</span>
      @if (linked()) {
        <span class="small text-success" [id]="provider() + '-linked'">{{ i18n.t('profile.external.linked') }}</span>
        <button type="button" class="btn btn-sm btn-outline-secondary" [id]="provider() + '-unlink'"
                [disabled]="isLast() || busy()" (click)="unlink()">
          {{ i18n.t('profile.external.unlink') }}
        </button>
      } @else {
        <span class="small text-body-secondary">{{ i18n.t('profile.external.notLinked') }}</span>
        @if (enabled()) {
          <button type="button" class="btn btn-sm btn-outline-primary" [id]="provider() + '-link'"
                  [disabled]="busy()" (click)="link()">
            {{ i18n.t('profile.external.link') }}
          </button>
        }
      }
    </div>
    @if (isLast()) {
      <div class="form-text" [id]="provider() + '-last-hint'">{{ i18n.t('profile.external.lastLoginHint') }}</div>
    }
    @if (error()) {
      <div class="text-danger small mt-1" [id]="provider() + '-error'">{{ error() }}</div>
    }
  `
})
export class ProfileExternalLoginComponent {
  protected readonly i18n = inject(I18nService);

  readonly provider = input.required<ExternalProvider>();
  readonly logins = input.required<AccountLoginsModel>();

  /** Площадка настроена на сервере: иначе «Привязать» некуда вести */
  readonly enabled = input(false);

  /** Отвязка прошла — новые «Основное» для родителя */
  readonly changed = output<AccountLoginsModel>();

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly titleKey = computed(() =>
    this.provider() === 'telegram' ? 'profile.external.telegram' as const : 'profile.external.vk' as const);

  protected readonly linked = computed(() =>
    this.provider() === 'telegram' ? this.logins().telegramLinked : this.logins().vkLinked);

  /** Этот вход — единственный способ войти: отвязать нельзя (раздел 3) */
  protected readonly isLast = computed(() => this.linked() && this.logins().loginCount <= 1);

  constructor(
    private account: AccountApiService,
    private external: ExternalLoginService,
    private fresh: FreshLoginService
  ) {}

  protected async link(): Promise<void> {
    if (!(await this.fresh.ensure())) return;

    this.error.set(null);
    this.busy.set(true);
    try {
      // Уход на площадку (Telegram — окно); вернётся человек на `/auth/{площадка}`
      await this.external.start(this.provider(), 'link', '/profile/edit');
    } catch (e) {
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.external.failed')));
    } finally {
      this.busy.set(false);
    }
  }

  protected async unlink(): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      const logins = await this.fresh.run(() => this.account.unlink(EXTERNAL_PROVIDER_CODES[this.provider()]));
      if (logins) this.changed.emit(logins);
    } catch (e) {
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.external.failed')));
    } finally {
      this.busy.set(false);
    }
  }
}
