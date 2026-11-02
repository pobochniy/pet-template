import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../core/admin-api.service';
import { AdminAccountCard, AdminReleasedContact } from '../core/admin.model';
import { apiErrorText } from '../core/api-error';

/**
 * Освобождение почты (спец. accounts, 8.4 и 8.5; admin, 12). Почта уникальна и она же
 * логин, поэтому чужой адрес можно вписать себе раньше владельца ящика. Владелец пишет
 * в поддержку — он и есть аккаунт карточки. Адрес снимается с аккаунта, где он не
 * подтверждён; подтверждённый сервер не снимет. Снятие и журнал у обоих аккаунтов —
 * одно сохранение на сервере
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'adm-release-email',
  imports: [FormsModule],
  template: `
    <div class="card card-body bg-white mb-3" id="release-email">
      <h4 class="h6">Освобождение почты</h4>

      @if (released(); as done) {
        <div class="alert alert-success small mb-0" id="release-done">
          Почта снята с аккаунта {{ done.releasedFromUserName }} — человек может вписать её себе.
        </div>
      } @else {
        <div class="small text-body-secondary mb-2">
          Адрес вписан в чужой аккаунт и там не подтверждён — снимаем его оттуда, чтобы человек вписал его себе.
        </div>
        <form class="row g-2 align-items-end" (ngSubmit)="release()">
          <div class="col-12">
            <label class="form-label small mb-0" for="release-address">Адрес почты</label>
            <input class="form-control form-control-sm" id="release-address" name="email" type="email" maxlength="255"
                   style="max-width: 360px" [(ngModel)]="email">
          </div>
          <div class="col-12">
            <label class="form-label small mb-0" for="release-reason">Причина (обязательна)</label>
            <input class="form-control form-control-sm" id="release-reason" name="reason" maxlength="500" [(ngModel)]="reason">
          </div>
          <div class="col-auto">
            <button class="btn btn-sm btn-primary" type="submit" id="release"
                    [disabled]="busy() || !email.trim() || !reason.trim()">Освободить почту</button>
          </div>
        </form>
      }

      @if (error()) {
        <div class="alert alert-danger small py-2 mt-2 mb-0">{{ error() }}</div>
      }
    </div>
  `
})
export class ReleaseEmailComponent {
  private readonly api = inject(AdminApiService);

  /** Тот, кто просит */
  readonly account = input.required<AdminAccountCard>();
  /** Почта снята — карточке перечитать журнал */
  readonly releasedChange = output<void>();

  protected readonly released = signal<AdminReleasedContact | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected email = '';
  protected reason = '';

  protected async release(): Promise<void> {
    if (!this.email.trim() || !this.reason.trim()) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      this.released.set(await this.api.releaseEmail(this.account().card.userId, {
        email: this.email.trim(),
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
