import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../core/admin-api.service';
import { AdminAccountCard, AdminReleasedContact } from '../core/admin.model';
import { apiErrorText } from '../core/api-error';

/**
 * Освобождение телефона (спец. admin, 12; `accounts.md`, раздел 6). Телефон
 * уникален и он же логин, а подтверждения у него нет — чужой номер можно вписать себе раньше
 * владельца. Владелец пишет в поддержку — он и есть аккаунт карточки. Номер снимается
 * с аккаунта, где он лежит; снятие и журнал у обоих аккаунтов — одно сохранение на сервере
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'adm-release-phone',
  imports: [FormsModule],
  template: `
    <div class="card card-body bg-white mb-3" id="release-phone">
      <h4 class="h6">Освобождение телефона</h4>

      @if (released(); as done) {
        <div class="alert alert-success small mb-0" id="release-phone-done">
          Телефон снят с аккаунта {{ done.releasedFromUserName }} — человек может вписать его себе.
        </div>
      } @else {
        <div class="small text-body-secondary mb-2">
          Номер вписан в чужой аккаунт — снимаем его оттуда, чтобы человек вписал его себе.
          Подтверждения у телефона нет: что номер его, проверьте сами.
        </div>
        <form class="row g-2 align-items-end" (ngSubmit)="release()">
          <div class="col-12">
            <label class="form-label small mb-0" for="release-phone-number">Телефон (+ и 11 цифр)</label>
            <input class="form-control form-control-sm" id="release-phone-number" name="phone" type="tel" maxlength="12"
                   style="max-width: 360px" [(ngModel)]="phone">
          </div>
          <div class="col-12">
            <label class="form-label small mb-0" for="release-phone-reason">Причина (обязательна)</label>
            <input class="form-control form-control-sm" id="release-phone-reason" name="reason" maxlength="500" [(ngModel)]="reason">
          </div>
          <div class="col-auto">
            <button class="btn btn-sm btn-primary" type="submit" id="release-phone-submit"
                    [disabled]="busy() || !phone.trim() || !reason.trim()">Освободить телефон</button>
          </div>
        </form>
      }

      @if (error()) {
        <div class="alert alert-danger small py-2 mt-2 mb-0">{{ error() }}</div>
      }
    </div>
  `
})
export class ReleasePhoneComponent {
  private readonly api = inject(AdminApiService);

  /** Тот, кто просит */
  readonly account = input.required<AdminAccountCard>();
  /** Телефон снят — карточке перечитать журнал */
  readonly releasedChange = output<void>();

  protected readonly released = signal<AdminReleasedContact | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected phone = '';
  protected reason = '';

  protected async release(): Promise<void> {
    if (!this.phone.trim() || !this.reason.trim()) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      this.released.set(await this.api.releasePhone(this.account().card.userId, {
        phone: this.phone.trim(),
        reason: this.reason.trim()
      }));
      this.releasedChange.emit();
    } catch (error) {
      this.error.set(apiErrorText(error));
    } finally {
      this.busy.set(false);
    }
  }
}
