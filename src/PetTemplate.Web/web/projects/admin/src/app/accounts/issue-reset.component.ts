import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../core/admin-api.service';
import { AdminAccountCard, AdminIssuedReset } from '../core/admin.model';
import { apiErrorText } from '../core/api-error';

/**
 * Выдача ссылки сброса из поддержки (спец. accounts, 8.5; admin, 12) — с обязательной
 * причиной. Ссылку админ отправляет сам, в тот же разговор, где попросили помощи:
 * PetTemplate её никуда не шлёт
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'adm-issue-reset',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="card card-body bg-white mb-3" id="issue-reset">
      <h4 class="h6">Ссылка сброса пароля</h4>

      @if (issued(); as link) {
        <div class="alert alert-success small mb-0">
          <div>{{ link.replaced ? 'Прежняя ссылка ещё жила — выдана новая взамен, с тем же сроком; прежняя больше не работает.' : '' }}
            Ссылка действует до {{ link.expiresAtUtc | date: 'dd.MM.yyyy HH:mm' }} — отправьте её человеку в тот же разговор:</div>
          <div class="input-group input-group-sm mt-2">
            <input class="form-control" readonly [value]="link.url" id="reset-url">
            <button class="btn btn-outline-secondary" type="button" id="reset-copy" (click)="copy(link.url)">
              {{ copied() ? 'Скопировано' : 'Скопировать' }}
            </button>
          </div>
        </div>
      } @else {
        <form class="row g-2 align-items-end" (ngSubmit)="issue()">
          <div class="col-12">
            <label class="form-label small mb-0" for="reason">Причина (обязательна)</label>
            <input class="form-control form-control-sm" id="reason" name="reason" maxlength="500" [(ngModel)]="reason">
          </div>
          <div class="col-auto">
            <button class="btn btn-sm btn-primary" type="submit" id="issue" [disabled]="busy() || !reason.trim()">
              Выдать ссылку сброса
            </button>
          </div>
        </form>
      }

      @if (error()) {
        <div class="alert alert-danger small py-2 mt-2 mb-0">{{ error() }}</div>
      }
    </div>
  `
})
export class IssueResetComponent {
  private readonly api = inject(AdminApiService);

  readonly account = input.required<AdminAccountCard>();
  /** Ссылка выдана — карточке перечитать журнал */
  readonly issuedChange = output<void>();

  protected readonly issued = signal<AdminIssuedReset | null>(null);
  protected readonly copied = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected reason = '';

  protected async issue(): Promise<void> {
    if (!this.reason.trim()) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      this.issued.set(await this.api.issueReset(this.account().card.userId, this.reason.trim()));
      this.issuedChange.emit();
    } catch (error) {
      this.error.set(apiErrorText(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(url: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(url);
      this.copied.set(true);
    } catch {
      // Буфер обмена недоступен — ссылка остаётся в поле, её можно выделить руками
    }
  }
}
