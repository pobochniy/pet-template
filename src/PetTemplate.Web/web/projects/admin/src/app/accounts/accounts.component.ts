import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AdminApiService } from '../core/admin-api.service';
import { AdminAccount, AdminAccountCard, AdminAccountWorkspace } from '../core/admin.model';
import { apiErrorText } from '../core/api-error';
import { actionTitle } from './action-titles';
import { IssueResetComponent } from './issue-reset.component';
import { ReleaseEmailComponent } from './release-email.component';
import { ReleasePhoneComponent } from './release-phone.component';

/**
 * Аккаунты (спец. accounts, 8.5; admin, 12): поиск по логину, почте, телефону, Telegram
 * или VK и карточка, по которой проверяют, что пишет владелец, — не раскрывая содержимого.
 * Правило проверки написано на самом экране.
 *
 * Карточка — та же, что у слияния и удаления (`AccountCards`): пространства с правами
 * и участниками, без содержимого. Числа своих сущностей проект дописывает в неё сам —
 * здесь их выводит `activity()`, если они есть.
 *
 * У аккаунта без пространств вместо ссылки сброса — «Удалить аккаунт»: восстанавливать
 * в нём нечего. У аккаунта с ролью в админке нет ни того, ни другого — сервер откажет.
 * «Освобождение почты» и «Освобождение телефона» — у любого аккаунта: карточка — того, кто
 * просит, а снимается адрес или номер с чужого аккаунта. Открытая карточка живёт в адресе (`?id=`)
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'adm-accounts',
  imports: [FormsModule, DatePipe, IssueResetComponent, ReleaseEmailComponent, ReleasePhoneComponent],
  template: `
    <h2 class="h6">Аккаунты</h2>

    <form class="d-flex gap-2 mb-3" style="max-width: 480px" (ngSubmit)="search()">
      <input class="form-control form-control-sm" name="query" placeholder="Логин, почта, телефон, id Telegram или VK"
             [(ngModel)]="query">
      <button class="btn btn-sm btn-outline-primary" type="submit" [disabled]="busy() || !query.trim()">Найти</button>
    </form>

    @if (error()) {
      <div class="alert alert-danger small py-2">{{ error() }}</div>
    }

    @if (found(); as found) {
      @if (found.length === 0) {
        <div class="text-body-secondary small mb-3">Ничего не нашлось.</div>
      } @else {
        <table class="table table-sm table-hover bg-white mb-3" style="max-width: 720px">
          <tbody>
            @for (account of found; track account.userId) {
              <tr class="found-row" role="button" (click)="open(account.userId)">
                <td>{{ account.userName }}</td>
                <td class="text-body-secondary">{{ account.email ?? '' }}</td>
                <td class="text-body-secondary">{{ account.phoneNumber ?? '' }}</td>
                <td class="text-body-secondary small">{{ logins(account) }}</td>
                <td class="text-end small">{{ account.registeredAtUtc | date: 'dd.MM.yyyy' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    }

    @if (card(); as account) {
      <div class="alert alert-info small" id="owner-rule">
        <b>Как проверить, что пишет владелец.</b> Пусть назовёт то, что есть только у него: названия
        пространств, логины их участников, когда заходил в последний раз. Если он в общем
        пространстве — подтверждение другого участника весомее любых ответов. Пароль не
        спрашивайте и не задавайте: человеку уходит только ссылка сброса.
      </div>

      <div class="card card-body bg-white mb-3" id="account-card">
        <div class="d-flex align-items-baseline gap-3 flex-wrap">
          <h3 class="h6 mb-0">{{ account.card.userName }}</h3>
          <span class="text-body-secondary small">
            с {{ account.card.registeredAtUtc | date: 'dd.MM.yyyy' }} ·
            последний вход {{ account.lastLoginAtUtc ? (account.lastLoginAtUtc | date: 'dd.MM.yyyy HH:mm') : 'неизвестен' }}
          </span>
        </div>
        <div class="small mt-1">
          Входы: {{ ways(account) }} ·
          почта {{ account.email ? account.email + (account.emailConfirmed ? ' (подтверждена)' : ' (не подтверждена)') : 'не указана' }} ·
          телефон {{ account.phoneNumber ?? 'не указан' }}
        </div>
        @if (activity(account); as counted) {
          <div class="small text-body-secondary" id="activity">Завёл сам: {{ counted }}</div>
        }

        <table class="table table-sm small mt-2 mb-0">
          <thead>
            <tr><th>Пространство</th><th>Роль</th><th>Участники</th></tr>
          </thead>
          <tbody>
            @for (workspace of account.card.workspaces; track workspace.id) {
              <tr class="workspace-row">
                <td>{{ workspace.name }}</td>
                <td>{{ workspace.isOwner ? 'владелец' : 'участник' }}</td>
                <td>{{ members(workspace) }}</td>
              </tr>
            } @empty {
              <tr><td colspan="3" class="text-body-secondary" id="no-workspaces">Пространств нет.</td></tr>
            }
          </tbody>
        </table>
      </div>

      @if (account.isAdmin) {
        <!-- Ни ссылки, ни удаления: сбросом пароля админ с ролью Accounts вошёл бы под админом с большими правами -->
        <div class="alert alert-secondary small" id="admin-account">
          У аккаунта есть роль в админке — ни ссылку сброса, ни удаление не делают: сначала роли снимает другой админ.
        </div>
      } @else if (account.card.workspaces.length > 0) {
        <!-- Аккаунт с пространствами восстанавливают ссылкой; без них — удаляют.
             Форма — своя у каждого аккаунта: выданная ссылка и причина не должны пережить переход к другому -->
        @for (current of [account]; track current.card.userId) {
          <adm-issue-reset [account]="current" (issuedChange)="reload()"></adm-issue-reset>
        }
      } @else if (!confirmingDelete()) {
        <form class="d-flex gap-2 align-items-end mb-3" style="max-width: 640px" (ngSubmit)="confirmingDelete.set(true)">
          <div class="flex-grow-1">
            <label class="form-label small mb-0" for="delete-reason">Причина (обязательна)</label>
            <input class="form-control form-control-sm" id="delete-reason" name="reason" maxlength="500" [(ngModel)]="reason">
          </div>
          <button class="btn btn-sm btn-outline-danger" type="submit" id="delete-account" [disabled]="busy() || !reason.trim()">
            Удалить аккаунт…
          </button>
        </form>
      } @else {
        <div class="alert alert-warning small d-flex align-items-center gap-2" id="delete-confirmation">
          <span class="me-auto">Удалить «{{ account.card.userName }}» навсегда: «{{ reason.trim() }}»?</span>
          <button class="btn btn-sm btn-danger" type="button" id="delete-confirm" (click)="remove()" [disabled]="busy()">Да, удалить</button>
          <button class="btn btn-sm btn-outline-secondary" type="button" (click)="confirmingDelete.set(false)">Отмена</button>
        </div>
      }

      <!-- Форма своя у каждого аккаунта, как у ссылки сброса -->
      @for (current of [account]; track current.card.userId) {
        <adm-release-email [account]="current" (releasedChange)="reload()"></adm-release-email>
        <adm-release-phone [account]="current" (releasedChange)="reload()"></adm-release-phone>
      }

      @if (account.actions.length > 0) {
        <h4 class="h6 mt-3">Журнал</h4>
        <ul class="small">
          @for (action of account.actions; track $index) {
            <li>{{ action.createdUtc | date: 'dd.MM.yyyy HH:mm' }} · {{ action.adminName ?? 'удалённый админ' }} ·
              {{ actionTitle(action.kind) }} · «{{ action.reason }}»</li>
          }
        </ul>
      }
    }

    @if (deleted()) {
      <div class="alert alert-success small" id="deleted">Аккаунт удалён. Логин свободен — человек может зарегистрироваться заново.</div>
    }
  `
})
export class AccountsComponent implements OnInit {
  protected readonly api = inject(AdminApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Чья карточка запрошена последней: ответ на прежний запрос её не перезапишет */
  private requested: string | null = null;

  protected query = '';
  protected reason = '';
  protected readonly found = signal<AdminAccount[] | null>(null);
  protected readonly card = signal<AdminAccountCard | null>(null);
  protected readonly confirmingDelete = signal(false);
  protected readonly deleted = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly actionTitle = actionTitle;

  /**
   * Карточка — по адресу, и при «Назад»/«Вперёд» тоже: компонент тот же, и без подписки
   * на экране осталась бы карточка B при `?id=A` — ссылка сброса ушла бы не тому
   */
  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const id = params.get('id');
      if (id && id !== this.requested) void this.load(id);
    });
  }

  protected logins(account: AdminAccount): string {
    return [account.hasTelegram ? 'Telegram' : null, account.hasVk ? 'VK' : null].filter(Boolean).join(', ');
  }

  protected ways(account: AdminAccountCard): string {
    const card = account.card;
    return [card.hasPassword ? 'пароль' : null, card.hasTelegram ? 'Telegram' : null, card.hasVk ? 'VK' : null]
      .filter(Boolean).join(', ') || 'нет';
  }

  protected members(workspace: AdminAccountWorkspace): string {
    return workspace.isSolo ? 'только он' : workspace.otherMembers.join(', ');
  }

  /**
   * Счётчики проекта из `activity` (в ядре их нет): `issues: 3, notes: 1` — как есть,
   * ненулевые. Пусто — строки на экране нет
   */
  protected activity(account: AdminAccountCard): string | null {
    const counted = Object.entries(account.card.activity ?? {}).filter(([, count]) => count > 0);
    return counted.length === 0 ? null : counted.map(([name, count]) => `${name}: ${count}`).join(', ');
  }

  protected async search(): Promise<void> {
    await this.run(async () => this.found.set(await this.api.searchAccounts(this.query.trim())));
  }

  protected async open(id: string): Promise<void> {
    // Запрос — до перехода: подписка на адрес увидит, что эта карточка уже грузится
    const loading = this.load(id);
    await this.router.navigate([], { relativeTo: this.route, queryParams: { id } });
    await loading;
  }

  /** Перечитать карточку — после выдачи ссылки или другой операции */
  async reload(): Promise<void> {
    const account = this.card();
    if (account) await this.load(account.card.userId);
  }

  protected async remove(): Promise<void> {
    const account = this.card();
    if (!account) return;

    const done = await this.run(() => this.api.deleteAccount(account.card.userId, this.reason.trim()));
    if (!done) return;

    this.card.set(null);
    this.requested = null;
    this.found.set(null);
    this.deleted.set(true);
    await this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  private async load(id: string): Promise<void> {
    this.requested = id;
    this.reason = '';
    this.confirmingDelete.set(false);
    this.deleted.set(false);
    await this.run(async () => {
      const card = await this.api.accountCard(id);
      if (this.requested === id) this.card.set(card);
    });
  }

  private async run(action: () => Promise<void>): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      return true;
    } catch (error) {
      this.error.set(apiErrorText(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
