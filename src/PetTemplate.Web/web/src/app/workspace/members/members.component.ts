import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { LucideAngularModule, Users } from 'lucide-angular';
import { Copy, KeyRound, Link2, UserMinus, UserPlus } from 'lucide-angular/src/icons';
import { InvitationApiService } from '../../shared/api/invitation-api.service';
import { UsersApiService } from '../../shared/api/users-api.service';
import { WorkspaceApiService } from '../../shared/api/workspace-api.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { I18nService } from '../../shared/i18n/i18n.service';
import { formatDate } from '../../shared/i18n/format.util';
import { UserService } from '../../shared/core/user.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { InvitationStatusEnum } from '../../shared/enums/invitation-status.enum';
import { PermissionEnum } from '../../shared/enums/permission.enum';
import { provideIcons } from '../../shared/icons/lucide-icons.provider';
import { UserProfileModel } from '../../shared/models/user-profile.model';
import { WorkspaceInvitationModel } from '../../shared/models/workspace-invitation.model';
import { PermissionsService } from '../../shared/services/permissions.service';
import { WorkspaceInviteFormComponent } from './invite-form/invite-form.component';
import { WorkspaceMemberPermissionsComponent } from './member-permissions/member-permissions.component';

/**
 * Участники пространства (`/workspace/members`, спец. authorization, 7.3): приглашение
 * нового, состав пространства и выданные ссылки — на одном экране. Вырос из экрана
 * `role-management` шаблона: правка прав теперь в строке участника
 * (`workspace-member-permissions`) и внутри активного пространства.
 *
 * Тот же компонент встраивается в настройки пространств при единственном пространстве
 * (`[embedded]="true"`): ходить за участниками на отдельную страницу, когда
 * пространство одно, незачем.
 *
 * Разделы закрыты каждый своим правом, поэтому маршрут пускает по любому из трёх:
 * приглашения — `WorkspaceMemberCrud`, правка прав и «Выгнать» — `PermissionManagement`,
 * состав — любое из них или `WorkspaceMemberRead`. Сам список отдельным правом не закрыт
 */
@Component({
  selector: 'workspace-members',
  templateUrl: './members.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule, WorkspaceInviteFormComponent, WorkspaceMemberPermissionsComponent],
  providers: [provideIcons({ Copy, KeyRound, Link2, UserMinus, UserPlus, Users })]
})
export class WorkspaceMembersComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly usersApi = inject(UsersApiService);
  private readonly workspaceApi = inject(WorkspaceApiService);
  private readonly invitationApi = inject(InvitationApiService);
  private readonly permissionsService = inject(PermissionsService);
  private readonly userService = inject(UserService);
  private readonly workspaceContext = inject(WorkspaceContextService);
  private readonly alerts = inject(AlertsService);

  /** Встроен в страницу настроек: своего контейнера и заголовка страницы не рисует */
  readonly embedded = input(false);

  readonly members = signal<UserProfileModel[]>([]);
  readonly invitations = signal<WorkspaceInvitationModel[]>([]);

  /** Только что выданная ссылка — её показываем с кнопкой копирования */
  readonly createdLink = signal<string | null>(null);
  readonly busy = signal(false);

  /**
   * Форма приглашения свёрнута в кнопку: звать нового участника нужно не каждый раз.
   * Развёрнутую обратно не сворачиваем — на полпути заполненную форму убирать
   * из-под рук нельзя, а перезагрузка страницы вернёт кнопку
   */
  readonly inviteOpen = signal(false);

  /** Участник, которому раскрыли права; редактор открыт один за раз */
  readonly editing = signal<string | null>(null);

  /** Участник, по которому нажали «Выгнать»: второй клик уже убирает */
  readonly kicking = signal<string | null>(null);

  /** Приглашение, отзыв которого ждёт подтверждения: удаление без вопроса не делаем нигде */
  readonly revoking = signal<string | null>(null);

  readonly canInvite = computed(() => this.permissionsService.permissions().includes(PermissionEnum.WorkspaceMemberCrud));

  readonly canManagePermissions = computed(
    () => this.permissionsService.permissions().includes(PermissionEnum.PermissionManagement));

  private readonly currentUserId = computed(() => this.userService.profile()?.id ?? null);

  constructor() {
    // Сменили пространство в шапке, не уходя со страницы, — состав и ссылки
    // перечитываем: страница берёт пространство из контекста
    let first = true;
    effect(() => {
      this.workspaceContext.currentWorkspaceId();
      if (first) {
        first = false;
        return;
      }

      untracked(() => {
        this.editing.set(null);
        this.kicking.set(null);
        this.createdLink.set(null);
        void this.load();
      });
    });
  }

  async ngOnInit() {
    await this.load();
  }

  isSelf(userId: string): boolean {
    return userId === this.currentUserId();
  }

  openInvite() {
    this.inviteOpen.set(true);
  }

  /** Ссылка готова: показываем её и перечитываем список выданных */
  async invitationCreated(invitation: WorkspaceInvitationModel) {
    this.createdLink.set(this.invitationApi.link(invitation.token));
    await this.loadInvitations();
  }

  startEdit(userId: string) {
    this.kicking.set(null);
    this.editing.set(userId);
  }

  stopEdit() {
    this.editing.set(null);
  }

  confirmKick(userId: string) {
    this.editing.set(null);
    this.kicking.set(userId);
  }

  cancelKick() {
    this.kicking.set(null);
  }

  /** Убрать участника из пространства (спец. authorization, 8) */
  async kick(userId: string) {
    const workspaceId = this.workspaceId();
    if (!workspaceId) return;

    this.busy.set(true);
    try {
      await this.workspaceApi.removeMember(userId, workspaceId);
      await this.loadMembers();

      this.kicking.set(null);
      this.alerts.push('success', this.i18n.t('workspace.members.removed'), 3000);
    } catch (error: unknown) {
      this.alerts.push('danger', serverErrorMessage(error, this.i18n, this.i18n.t('workspace.error.kickFailed')), 5000);
    } finally {
      this.busy.set(false);
    }
  }

  async revoke(invitation: WorkspaceInvitationModel) {
    const workspaceId = this.workspaceId();
    if (!workspaceId) return;

    this.revoking.set(null);
    try {
      await this.invitationApi.revoke(invitation.id, workspaceId);
      await this.loadInvitations();
    } catch (error: unknown) {
      // «Принятое приглашение отозвать нельзя» приезжает в теле ответа
      this.alerts.push('danger', serverErrorMessage(error, this.i18n, this.i18n.t('workspace.error.revokeFailed')), 5000);
    }
  }

  async copy(token: string) {
    await this.copyLink(this.invitationApi.link(token));
  }

  async copyCreated() {
    const link = this.createdLink();
    if (link) await this.copyLink(link);
  }

  /** Строка состояния приглашения для списка выданных */
  statusText(invitation: WorkspaceInvitationModel): string {
    switch (invitation.status) {
      case InvitationStatusEnum.Accepted:
        return this.i18n.t('workspace.invitation.accepted', {
          user: invitation.acceptedByUserName ?? this.i18n.t('workspace.invitation.acceptedByMember')
        });
      case InvitationStatusEnum.Revoked:
        return this.i18n.t('workspace.invitation.revoked');
      case InvitationStatusEnum.Expired:
        return this.i18n.t('workspace.invitation.expired');
      default:
        return this.i18n.t('workspace.invitation.active', {
          date: formatDate(this.i18n.lang(), invitation.expiresAtUtc, { dateStyle: 'medium', timeStyle: 'short' })
        });
    }
  }

  /**
   * Живая ссылка: только с ней есть что делать — скопировать и отозвать. По принятой
   * уже пришли (приглашение одноразовое), отозванная и просроченная не сработают.
   * В списке такие строки остаются — это история, кого и когда позвали, — но без кнопок
   */
  isActive(invitation: WorkspaceInvitationModel): boolean {
    return invitation.status === InvitationStatusEnum.Active;
  }

  private async load() {
    await Promise.all([this.loadMembers(), this.loadInvitations()]);
  }

  private async loadMembers() {
    if (!this.workspaceId()) return;

    try {
      this.members.set(await this.usersApi.getProfiles());

      // Один участник — это сам пользователь: звать некого, и прятать форму
      // приглашения за кнопкой было бы издевательством
      if (this.members().length <= 1) this.inviteOpen.set(true);
    } catch {
      this.alerts.push('danger', this.i18n.t('workspace.members.loadFailed'), 5000);
    }
  }

  private async loadInvitations() {
    const workspaceId = this.workspaceId();

    // Раздел ссылок на клиенте закрыт правом выдачи (спец. authorization, 7.3),
    // хотя чтение на сервере открыто и `WorkspaceMemberRead`
    if (!workspaceId || !this.canInvite()) {
      this.invitations.set([]);
      return;
    }

    try {
      this.invitations.set(await this.invitationApi.getList(workspaceId));
    } catch {
      this.alerts.push('danger', this.i18n.t('workspace.members.invitationsLoadFailed'), 5000);
    }
  }

  private workspaceId(): string | null {
    return this.workspaceContext.getCurrentWorkspaceId();
  }

  /**
   * Ссылку отправляют через сторонние приложения — копируем в буфер. Не вышло —
   * ссылка остаётся на экране, её можно выделить руками, поэтому это не ошибка
   */
  private async copyLink(url: string) {
    try {
      await navigator.clipboard.writeText(url);
      this.alerts.push('success', this.i18n.t('workspace.members.copied'), 3000);
    } catch {
      this.alerts.push('warning', this.i18n.t('workspace.members.copyFailed'), 5000);
    }
  }
}
