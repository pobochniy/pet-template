import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RecoveryApiService } from '../../shared/api/recovery-api.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';

/**
 * `/auth/confirm-email/:token` — переход по ссылке из письма (спец. accounts, 8.4).
 * Без входа: письмо часто открывают на телефоне, где человек не вошёл
 */
@Component({
  selector: 'auth-confirm-email',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: ['.auth-confirm { max-width: 520px; }'],
  template: `
    <div class="container py-5 text-center auth-confirm">
      @switch (state()) {
        @case ('pending') {
          <p class="text-body-secondary" id="confirm-pending">{{ i18n.t('core.empty.loading') }}</p>
        }
        @case ('done') {
          <div class="alert alert-success" id="confirm-done">{{ i18n.t('auth.confirmEmail.done') }}</div>
          <a class="btn btn-primary" [routerLink]="['/']">{{ i18n.t('auth.confirmEmail.toHome') }}</a>
        }
        @case ('failed') {
          <div class="alert alert-danger" id="confirm-failed">{{ error() }}</div>
          <a class="btn btn-outline-primary" [routerLink]="['/profile']">{{ i18n.t('auth.confirmEmail.toProfile') }}</a>
        }
      }
    </div>
  `
})
export class ConfirmEmailComponent implements OnInit {
  protected readonly i18n = inject(I18nService);

  /** Токен из адреса — через `withComponentInputBinding()` */
  readonly token = input.required<string>();

  protected readonly state = signal<'pending' | 'done' | 'failed'>('pending');
  protected readonly error = signal('');

  constructor(private recovery: RecoveryApiService) {}

  async ngOnInit(): Promise<void> {
    try {
      await this.recovery.confirmEmail(this.token());
      this.state.set('done');
    } catch (e) {
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('auth.error.linkInvalid')));
      this.state.set('failed');
    }
  }
}
