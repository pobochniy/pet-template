import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../core/admin-api.service';
import { AdminAccessLogEntry, AdminAccessLogFilter, AdminMember } from '../core/admin.model';
import { apiErrorText } from '../core/api-error';

/**
 * Журнал доступа к диалогам (спец. admin, 16.2): кто, когда, чей прогон открыл.
 * Под `AdminManagement`, а не под `Dialogs` — смысл записи в том, что её смотрит не
 * тот, кто её оставил. Плоский список с фильтром по админу и периоду; удалять из него
 * нечем, кроме срока хранения, поэтому и кнопок здесь нет
 */
@Component({
  selector: 'adm-access-log',
  imports: [FormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="d-flex flex-wrap align-items-end gap-2 mb-3" (ngSubmit)="search()">
      <h2 class="h6 mb-0 me-auto">Журнал доступа</h2>
      <select class="form-select form-select-sm w-auto" name="admin" [(ngModel)]="adminUserId">
        <option [ngValue]="null">все админы</option>
        @for (admin of admins(); track admin.userId) {
          <option [ngValue]="admin.userId">{{ admin.userName }}</option>
        }
      </select>
      <input class="form-control form-control-sm w-auto" type="date" name="from" [(ngModel)]="from">
      <input class="form-control form-control-sm w-auto" type="date" name="to" [(ngModel)]="to">
      <button class="btn btn-sm btn-outline-primary" type="submit" [disabled]="busy()">Найти</button>
    </form>

    @if (error()) {
      <div class="alert alert-danger small py-2">{{ error() }}</div>
    }

    <table class="table table-sm bg-white small">
      <thead>
        <tr>
          <th>Когда</th>
          <th>Кто</th>
          <th>Чей прогон</th>
          <th>Прогон</th>
        </tr>
      </thead>
      <tbody>
        @for (entry of entries(); track entry.id) {
          <tr class="entry-row">
            <td class="text-nowrap">{{ entry.createdUtc | date: 'dd.MM.yyyy HH:mm:ss' }}</td>
            <td>{{ entry.adminName ?? (entry.adminUserId ?? 'удалённый админ') }}</td>
            <td>{{ entry.workspaceName ? '«' + entry.workspaceName + '»' : 'пространство ' + entry.workspaceId }}</td>
            <td class="text-body-secondary">{{ entry.runId }}</td>
          </tr>
        } @empty {
          <tr><td colspan="4" class="text-body-secondary">Записей нет.</td></tr>
        }
      </tbody>
    </table>

    @if (nextBefore()) {
      <button class="btn btn-sm btn-outline-secondary" type="button" (click)="more()" [disabled]="busy()">Показать ещё</button>
    }
  `
})
export class AccessLogComponent implements OnInit {
  private readonly api = inject(AdminApiService);

  protected adminUserId: string | null = null;
  protected from = '';
  protected to = '';

  /**
   * Фильтр, по которому построен показанный список: «Показать ещё» продолжает его,
   * а не форму — поправленный, но не отправленный фильтр смешал бы две выборки
   */
  private applied: AdminAccessLogFilter = this.filter();

  protected readonly admins = signal<AdminMember[]>([]);
  protected readonly entries = signal<AdminAccessLogEntry[]>([]);
  protected readonly nextBefore = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.run(async () => {
      const [admins] = await Promise.all([this.api.admins(), this.fetch()]);
      this.admins.set(admins);
    });
  }

  protected async search(): Promise<void> {
    await this.run(() => this.fetch());
  }

  protected async more(): Promise<void> {
    const before = this.nextBefore();
    if (!before) return;

    await this.run(async () => {
      const page = await this.api.accessLog(this.applied, before);
      this.entries.update(entries => [...entries, ...page.entries]);
      this.nextBefore.set(page.nextBefore);
    });
  }

  private async fetch(): Promise<void> {
    this.applied = this.filter();
    const page = await this.api.accessLog(this.applied);
    this.entries.set(page.entries);
    this.nextBefore.set(page.nextBefore);
  }

  private filter(): AdminAccessLogFilter {
    return { adminUserId: this.adminUserId, from: this.from || null, to: this.to || null };
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
