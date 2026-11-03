import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { AdminApiService } from '../core/admin-api.service';
import { AdminRole, ROLE_TITLES } from '../core/admin-role';
import { AdminSessionService } from '../core/admin-session.service';
import { AdminSummary } from '../core/admin.model';
import { apiErrorText } from '../core/api-error';

/**
 * «Сводка» — единственный экран каркаса (спец. admin, 4 и 6): что сейчас отвечает
 * на сервере и кто вошёл. Открыта всякому админу — роль раздела `null`. Реиндекс поиска
 * (спец. search, 5) — только с ролью «Управление админами»
 */
@Component({
  selector: 'adm-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  template: `
    <h2 class="h6">Сводка</h2>
    @if (error()) {
      <div class="alert alert-danger small">Сводка не загрузилась: {{ error() }}</div>
    }
    <table class="table table-sm w-auto">
      <tbody>
        <tr>
          <th class="fw-normal text-body-secondary">Версия API</th>
          <td id="api-version">{{ summary()?.version || '—' }}</td>
        </tr>
        <tr>
          <th class="fw-normal text-body-secondary">Время сервера</th>
          <td id="server-time">{{ summary()?.serverTimeUtc | date: 'dd.MM.yyyy HH:mm:ss' }}</td>
        </tr>
        <tr>
          <th class="fw-normal text-body-secondary">Вошёл</th>
          <td id="admin-name">{{ session.admin()?.userName }}</td>
        </tr>
        <tr>
          <th class="fw-normal text-body-secondary">Роли</th>
          <td id="admin-roles">{{ roles() }}</td>
        </tr>
        <tr>
          <th class="fw-normal text-body-secondary">Сессия до</th>
          <td id="session-until">{{ session.expiresAt | date: 'dd.MM.yyyy HH:mm' }}</td>
        </tr>
      </tbody>
    </table>
    @if (session.hasRole(adminRole.AdminManagement)) {
      <h2 class="h6 mt-4">Поиск</h2>
      <p class="small text-body-secondary mb-2">
        Все записи с текстом заново уходят в векторный индекс. Нужно после смены модели эмбеддингов,
        после потери тома Qdrant и после подключения раздела к поиску: старые записи в индекс сами
        не попадают.
      </p>
      <button type="button" class="btn btn-sm btn-outline-primary" id="reindex-search"
              [disabled]="reindexing()" (click)="reindex()">Переиндексировать</button>
      @if (reindexed()) {
        <span class="small text-success ms-2" id="reindex-queued">В очереди</span>
      }
      @if (reindexError()) {
        <div class="alert alert-danger small mt-2" id="reindex-error">Не поставилось: {{ reindexError() }}</div>
      }
    }
  `
})
export class SummaryComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  protected readonly session = inject(AdminSessionService);

  protected readonly summary = signal<AdminSummary | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly adminRole = AdminRole;
  protected readonly reindexing = signal(false);
  protected readonly reindexed = signal(false);
  protected readonly reindexError = signal<string | null>(null);

  /** Роли — из токена, как их видит сервер: снятая живёт до конца часа (спец. admin, 3.3) */
  protected readonly roles = computed(() =>
    (this.session.admin()?.roles ?? []).map(role => ROLE_TITLES[role] ?? String(role)).join(', ')
  );

  async ngOnInit(): Promise<void> {
    try {
      this.summary.set(await this.api.summary());
    } catch (error) {
      this.error.set(apiErrorText(error));
    }
  }

  /** Ответ значит «в очереди», а не «готово»: записи разбирает воркер (спец. search, 5) */
  protected async reindex(): Promise<void> {
    this.reindexing.set(true);
    this.reindexed.set(false);
    this.reindexError.set(null);
    try {
      await this.api.reindexSearch();
      this.reindexed.set(true);
    } catch (error) {
      this.reindexError.set(apiErrorText(error));
    } finally {
      this.reindexing.set(false);
    }
  }
}
