import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../core/admin-api.service';
import { AdminInvitationStatus, STATUS_TITLES } from '../core/admin-invitation-status';
import { ADMIN_ROLES, AdminRole, ROLE_TITLES } from '../core/admin-role';
import { AdminInvitation, AdminMember, IssuedAdminInvitation } from '../core/admin.model';
import { AdminSessionService } from '../core/admin-session.service';
import { apiErrorText } from '../core/api-error';

/**
 * Админы и приглашения (спец. admin, 3.1 и 3.4) — одна вкладка: кто уже админ
 * и с какими ролями, и выданные ссылки со статусами.
 *
 * Ссылка видна один раз — сразу после выдачи: сервер хранит только хеш токена, и в
 * списке её нет. Потерял — отзови и выдай новую.
 *
 * Выдать можно только свои роли — и в приглашении, и существующему админу; список
 * «своих» берётся из сессии, а сервер сверяет его с базой. Последнюю роль крестиком
 * не снять: удаление админа — отдельная кнопка с подтверждением
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'adm-admins',
  imports: [FormsModule, DatePipe],
  template: `
    <h2 class="h6">Админы</h2>

    @if (error()) {
      <div class="alert alert-danger small py-2">{{ error() }}</div>
    }

    <table class="table table-sm bg-white small mb-4" style="max-width: 960px">
      <thead>
        <tr>
          <th>Кто</th>
          <th>Роли</th>
          <th>Выдать ещё</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        @for (admin of admins(); track admin.userId) {
          <tr class="admin-row">
            <td>{{ admin.userName }}</td>
            <td>
              @for (granted of admin.roles; track granted.role) {
                <span class="badge text-bg-light border me-1 role"
                      [title]="'выдана ' + (granted.grantedUtc | date: 'dd.MM.yyyy') + (granted.grantedByName ? ', ' + granted.grantedByName : '')">
                  {{ roleTitle(granted.role) }}
                  @if (admin.roles.length > 1) {
                    <button class="btn btn-link btn-sm p-0 ms-1 text-danger revoke-role" type="button"
                            [title]="'Снять «' + roleTitle(granted.role) + '»'"
                            (click)="revokeRole(admin, granted.role)" [disabled]="busy()">×</button>
                  }
                </span>
              }
            </td>
            <td>
              @if (grantable(admin).length > 0) {
                <select class="form-select form-select-sm d-inline-block w-auto grant-role"
                        [disabled]="busy()" (change)="grantRole(admin, $event)">
                  <option value="">—</option>
                  @for (role of grantable(admin); track role) {
                    <option [value]="role">{{ roleTitle(role) }}</option>
                  }
                </select>
              }
            </td>
            <td class="text-end">
              @if (removing() === admin.userId) {
                <span class="confirmation">
                  Убрать {{ admin.userName }} из админки?
                  <button class="btn btn-sm btn-danger ms-1" type="button" (click)="remove(admin)" [disabled]="busy()">Да, убрать</button>
                  <button class="btn btn-sm btn-outline-secondary ms-1" type="button" (click)="removing.set(null)">Отмена</button>
                </span>
              } @else {
                <button class="btn btn-sm btn-outline-danger" type="button" (click)="removing.set(admin.userId)"
                        [disabled]="busy()">Убрать…</button>
              }
            </td>
          </tr>
        }
      </tbody>
    </table>

    <h2 class="h6">Приглашения</h2>

    <form class="d-flex flex-wrap align-items-center gap-3 mb-2 invite-form" (ngSubmit)="invite()">
      @for (role of ownRoles(); track role) {
        <div class="form-check mb-0">
          <input class="form-check-input" type="checkbox" [id]="'invite-role-' + role" [name]="'role-' + role"
                 [ngModel]="picked().has(role)" (ngModelChange)="toggle(role)">
          <label class="form-check-label small" [for]="'invite-role-' + role">{{ roleTitle(role) }}</label>
        </div>
      }
      <button class="btn btn-sm btn-primary" type="submit" [disabled]="busy() || picked().size === 0">Выдать ссылку</button>
    </form>
    <div class="text-body-secondary small mb-2">
      Ссылка одноразовая, живёт 3 дня и видна только сразу после выдачи — потом её не посмотреть. Принимается логином и паролем от продукта — и только пока админка открыта
      (workflow Admin Access).
    </div>

    @if (issued(); as issued) {
      <div class="alert alert-success small py-2 d-flex align-items-center gap-2 issued" style="max-width: 960px">
        <code class="me-auto">{{ link(issued) }}</code>
        <button class="btn btn-sm btn-outline-success" type="button" (click)="copy(issued)">Скопировать</button>
      </div>
    }

    <table class="table table-sm bg-white small" style="max-width: 960px">
      <thead>
        <tr>
          <th>Выдано</th>
          <th>Роли</th>
          <th>Статус</th>
          <th>Кто выдал</th>
          <th>Действует до</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        @for (invitation of invitations(); track invitation.id) {
          <tr class="invitation-row">
            <td class="text-nowrap">{{ invitation.createdUtc | date: 'dd.MM.yyyy HH:mm' }}</td>
            <td>{{ rolesTitle(invitation.roles) }}</td>
            <td>
              {{ statusTitle(invitation.status) }}{{ byName(invitation) ? ' · ' + byName(invitation) : '' }}
            </td>
            <td>{{ invitation.createdByName ?? '' }}</td>
            <td class="text-nowrap">{{ invitation.expiresUtc | date: 'dd.MM.yyyy HH:mm' }}</td>
            <td class="text-end text-nowrap">
              @if (invitation.status === Status.Pending) {
                <button class="btn btn-sm btn-outline-danger revoke-invitation" type="button"
                        (click)="revoke(invitation)" [disabled]="busy()">Отозвать</button>
              }
            </td>
          </tr>
        } @empty {
          <tr><td colspan="6" class="text-body-secondary">Приглашений нет.</td></tr>
        }
      </tbody>
    </table>
  `
})
export class AdminsComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly session = inject(AdminSessionService);

  protected readonly Status = AdminInvitationStatus;

  protected readonly admins = signal<AdminMember[]>([]);
  protected readonly invitations = signal<AdminInvitation[]>([]);

  /** Какого админа сейчас подтверждают удалить */
  protected readonly removing = signal<string | null>(null);

  protected readonly picked = signal(new Set<AdminRole>());
  protected readonly issued = signal<IssuedAdminInvitation | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Свои роли — из сессии; выдать больше сервер всё равно не даст */
  protected readonly ownRoles = computed(() => ADMIN_ROLES.filter(role => this.session.hasRole(role)));

  async ngOnInit(): Promise<void> {
    await this.run(() => this.reload());
  }

  protected roleTitle(role: AdminRole): string {
    return ROLE_TITLES[role] ?? `Роль ${role}`;
  }

  protected rolesTitle(roles: AdminRole[]): string {
    return roles.map(role => this.roleTitle(role)).join(', ');
  }

  protected statusTitle(status: AdminInvitationStatus): string {
    return STATUS_TITLES[status] ?? `Статус ${status}`;
  }

  /** Кто закрыл ссылку: принял её или отозвал */
  protected byName(invitation: AdminInvitation): string | null {
    return invitation.acceptedByName ?? invitation.revokedByName;
  }

  /** Свои роли, которых у админа ещё нет */
  protected grantable(admin: AdminMember): AdminRole[] {
    return this.ownRoles().filter(role => !admin.roles.some(granted => granted.role === role));
  }

  protected link(invitation: IssuedAdminInvitation): string {
    return `${location.origin}/invite/${invitation.token}`;
  }

  protected toggle(role: AdminRole): void {
    this.picked.update(picked => {
      const next = new Set(picked);
      if (!next.delete(role)) next.add(role);
      return next;
    });
  }

  protected async grantRole(admin: AdminMember, event: Event): Promise<void> {
    const select = event.target as HTMLSelectElement;
    const role = Number(select.value) as AdminRole;
    select.value = '';
    if (!role) return;

    await this.run(async () => {
      await this.api.grantRole(admin.userId, role);
      await this.reload();
    });
  }

  protected async revokeRole(admin: AdminMember, role: AdminRole): Promise<void> {
    await this.run(async () => {
      await this.api.revokeRole(admin.userId, role);
      await this.reload();
    });
  }

  protected async remove(admin: AdminMember): Promise<void> {
    await this.run(async () => {
      await this.api.removeAdmin(admin.userId);
      this.removing.set(null);
      await this.reload();
    });
  }

  protected async invite(): Promise<void> {
    const roles = ADMIN_ROLES.filter(role => this.picked().has(role));

    await this.run(async () => {
      this.issued.set(await this.api.createInvitation(roles));
      this.picked.set(new Set());
      this.invitations.set(await this.api.invitations());
    });
  }

  protected async revoke(invitation: AdminInvitation): Promise<void> {
    await this.run(async () => {
      await this.api.revokeInvitation(invitation.id);
      if (this.issued()?.id === invitation.id) this.issued.set(null);
      this.invitations.set(await this.api.invitations());
    });
  }

  protected async copy(invitation: IssuedAdminInvitation): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.link(invitation));
    } catch {
      // Буфер обмена недоступен (не https, запрет браузера) — ссылку видно на экране
      this.issued.set(invitation);
    }
  }

  private async reload(): Promise<void> {
    const [admins, invitations] = await Promise.all([this.api.admins(), this.api.invitations()]);
    this.admins.set(admins);
    this.invitations.set(invitations);
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
