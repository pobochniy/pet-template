import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminApiService } from '../core/admin-api.service';
import { AdminAccountJournalEntry, AdminAccountJournalFilter } from '../core/admin.model';
import { apiErrorText } from '../core/api-error';
import { actionTitle } from '../accounts/action-titles';

/**
 * Журнал аккаунтов (спец. accounts, 8.5 и 11; admin, 12): что админы делали с чужими
 * аккаунтами и какие аккаунты слились — одной лентой, новые сверху. Карточка удалённого
 * аккаунта — 404, и запись о его удалении или слиянии видна только здесь. Логин цели —
 * копия из журнала; аккаунт ещё есть — ссылка на его карточку в «Аккаунтах».
 *
 * Поиск — по части логина или по id аккаунта целиком. Только чтение, кнопок у строк нет
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'adm-account-journal',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <form class="d-flex flex-wrap align-items-end gap-2 mb-3" (ngSubmit)="search()">
      <h2 class="h6 mb-0 me-auto">Журнал аккаунтов</h2>
      <input class="form-control form-control-sm w-auto" name="query" placeholder="Логин или id аккаунта"
             [(ngModel)]="query">
      <button class="btn btn-sm btn-outline-primary" type="submit" [disabled]="busy()">Найти</button>
    </form>

    @if (error()) {
      <div class="alert alert-danger small py-2">{{ error() }}</div>
    }

    <table class="table table-sm bg-white small">
      <thead>
        <tr>
          <th>Когда</th>
          <th>Что</th>
          <th>Кому</th>
          <th>Кто</th>
          <th>Причина</th>
        </tr>
      </thead>
      <tbody>
        @for (entry of entries(); track entry.id) {
          <tr class="entry-row">
            <td class="text-nowrap">{{ entry.createdUtc | date: 'dd.MM.yyyy HH:mm' }}</td>
            @if (entry.kind === null) {
              <!-- Слияние делает сам человек, не админ: «кто» пуст, площадка — чем доказал владение вторым -->
              <td>
                слияние: {{ entry.targetUserName }} удалён,
                @if (entry.keptUserId) {
                  <a class="kept-link" routerLink="/accounts" [queryParams]="{ id: entry.keptUserId }">{{ entry.keptUserName || entry.keptUserId }}</a>
                } @else {
                  <span class="text-body-secondary">{{ entry.keptUserName || 'удалённый позже аккаунт' }} (аккаунта нет)</span>
                }
                остался
                @if (entry.platform) {
                  <div class="text-body-secondary">доказано: {{ entry.platform }}</div>
                }
              </td>
              <td>
                {{ entry.targetUserName }} <span class="text-body-secondary">(аккаунта нет)</span>
                <div class="text-body-secondary">{{ entry.targetUserId }}</div>
              </td>
              <td class="text-body-secondary">сам человек</td>
              <td></td>
            } @else {
              <td>{{ actionTitle(entry.kind) }}</td>
              <td>
                @if (entry.targetExists) {
                  <a class="target-link" routerLink="/accounts" [queryParams]="{ id: entry.targetUserId }">{{ entry.targetUserName }}</a>
                } @else {
                  {{ entry.targetUserName }} <span class="text-body-secondary">(аккаунта нет)</span>
                }
                <div class="text-body-secondary">{{ entry.targetUserId }}</div>
              </td>
              <td>{{ entry.adminName ?? (entry.adminUserId ?? 'удалённый админ') }}</td>
              <td>«{{ entry.reason }}»</td>
            }
          </tr>
        } @empty {
          <tr><td colspan="5" class="text-body-secondary">Записей нет.</td></tr>
        }
      </tbody>
    </table>

    @if (nextBefore()) {
      <button class="btn btn-sm btn-outline-secondary" type="button" (click)="more()" [disabled]="busy()">Показать ещё</button>
    }
  `
})
export class AccountJournalComponent implements OnInit {
  private readonly api = inject(AdminApiService);

  protected readonly actionTitle = actionTitle;
  protected query = '';

  /**
   * Поиск, по которому построен показанный список: «Показать ещё» продолжает его, а не
   * поле — поправленный, но не отправленный поиск смешал бы две выборки
   */
  private applied: AdminAccountJournalFilter = this.filter();

  protected readonly entries = signal<AdminAccountJournalEntry[]>([]);
  protected readonly nextBefore = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.run(() => this.fetch());
  }

  protected async search(): Promise<void> {
    await this.run(() => this.fetch());
  }

  protected async more(): Promise<void> {
    const before = this.nextBefore();
    if (!before) return;

    await this.run(async () => {
      const page = await this.api.accountJournal(this.applied, before);
      this.entries.update(entries => [...entries, ...page.entries]);
      this.nextBefore.set(page.nextBefore);
    });
  }

  private async fetch(): Promise<void> {
    this.applied = this.filter();
    const page = await this.api.accountJournal(this.applied);
    this.entries.set(page.entries);
    this.nextBefore.set(page.nextBefore);
  }

  private filter(): AdminAccountJournalFilter {
    return { query: this.query.trim() || null };
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (error) {
      this.error.set(apiErrorText(error));
    } finally {
      this.busy.set(false);
    }
  }
}
