import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import Copy from 'lucide/dist/esm/icons/copy.mjs';
import KeyRound from 'lucide/dist/esm/icons/key-round.mjs';
import Link2 from 'lucide/dist/esm/icons/link-2.mjs';
import UserMinus from 'lucide/dist/esm/icons/user-minus.mjs';
import UserPlus from 'lucide/dist/esm/icons/user-plus.mjs';
import Users from 'lucide/dist/esm/icons/users.mjs';
import { InvitationApiService } from '../../shared/api/invitation-api.service';
import { UsersApiService } from '../users-api.service';
import { WorkspaceApiService } from '../../shared/api/workspace-api.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { formatDate } from '../../shared/i18n/format.util';
import { UserService } from '../../shared/core/user.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { contextResource, contextSignal } from '../../shared/core/context-resource';
import { InvitationStatusEnum } from '../../shared/enums/invitation-status.enum';
import { PermissionEnum } from '../../shared/enums/permission.enum';
import { IconComponent } from '../../shared/icons/icon.component';
import { UserProfileModel } from '../../shared/models/user-profile.model';
import { WorkspaceInvitationModel } from '../../shared/models/workspace-invitation.model';
import { PermissionsService } from '../../shared/services/permissions.service';
import { WorkspaceInviteFormComponent } from './invite-form/invite-form.component';
import { WorkspaceMemberPermissionsComponent } from './member-permissions/member-permissions.component';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { WORKSPACE_SCOPES } from '../workspace.scopes';

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
  imports: [IconComponent, WorkspaceInviteFormComponent, WorkspaceMemberPermissionsComponent]
})
export class WorkspaceMembersComponent {
  protected readonly icons = { Copy, KeyRound, Link2, UserMinus, UserPlus, Users };

  protected readonly i18n = injectI18n(...WORKSPACE_SCOPES);
  private readonly usersApi = inject(UsersApiService);
  private readonly workspaceApi = inject(WorkspaceApiService);
  private readonly invitationApi = inject(InvitationApiService);
  private readonly permissionsService = inject(PermissionsService);
  private readonly userService = inject(UserService);
  private readonly workspaceContext = inject(WorkspaceContextService);
  private readonly alerts = inject(AlertsService);

  /** Встроен в страницу настроек: своего контейнера и заголовка страницы не рисует */
  readonly embedded = input(false);

  readonly canInvite = computed(() => this.permissionsService.permissions().includes(PermissionEnum.WorkspaceMemberCrud));

  readonly canManagePermissions = computed(
    () => this.permissionsService.permissions().includes(PermissionEnum.PermissionManagement));

  /**
   * Состав и ссылки — чтение, привязанное к пространству вкладки: сменили его в шапке,
   * не уходя со страницы, — прежний запрос обрывается, а ответ про прошлое пространство
   * в экран не ляжет (`contextResource`)
   */
  private readonly membersResource = contextResource<UserProfileModel[]>({
    loader: ({ abortSignal }) => this.usersApi.getProfiles(abortSignal),
    defaultValue: []
  });

  /**
   * Раздел ссылок на клиенте закрыт правом выдачи (спец. authorization, 7.3),
   * хотя чтение на сервере открыто и `WorkspaceMemberRead`: без права запроса нет
   */
  private readonly invitationsResource = contextResource<WorkspaceInvitationModel[], true>({
    params: () => (this.canInvite() ? true : undefined),
    loader: ({ workspaceId, abortSignal }) => this.invitationApi.getList(workspaceId, abortSignal),
    defaultValue: []
  });

  readonly members = this.membersResource.value;
  readonly invitations = this.invitationsResource.value;

  /** Контекст вкладки — от него сбрасывается всё, что относилось к прошлому пространству */
  private readonly context = this.userService.context;

  /** Только что выданная ссылка — её показываем с кнопкой копирования */
  readonly createdLink = contextSignal<string | null>(null);
  readonly busy = signal(false);

  /**
   * Форма приглашения свёрнута в кнопку: звать нового участника нужно не каждый раз.
   * Развёрнутую обратно не сворачиваем — на полпути заполненную форму убирать
   * из-под рук нельзя, а перезагрузка страницы вернёт кнопку.
   *
   * Один участник — это сам пользователь: звать некого, и прятать форму
   * приглашения за кнопкой было бы издевательством
   */
  private readonly inviteRequested = signal(false);
  readonly inviteOpen = computed(() =>
    this.inviteRequested() || (this.membersResource.status() === 'resolved' && this.members().length <= 1));

  /** Участник, которому раскрыли права; редактор открыт один за раз */
  readonly editing = contextSignal<string | null>(null);

  /** Участник, по которому нажали «Выгнать»: второй клик уже убирает */
  readonly kicking = contextSignal<string | null>(null);

  /** Приглашение, отзыв которого ждёт подтверждения: удаление без вопроса не делаем нигде */
  readonly revoking = contextSignal<string | null>(null);

  private readonly currentUserId = computed(() => this.context().userId);

  constructor() {
    // Ошибка чтения — плашкой, один раз на неудачную загрузку
    effect(() => {
      if (this.membersResource.error()) {
        untracked(() => this.alerts.push('danger', this.i18n.t('workspace.members.loadFailed'), 5000));
      }
    });
    effect(() => {
      if (this.invitationsResource.error()) {
        untracked(() => this.alerts.push('danger', this.i18n.t('workspace.members.invitationsLoadFailed'), 5000));
      }
    });
  }

  isSelf(userId: string): boolean {
    return userId === this.currentUserId();
  }

  openInvite() {
    this.inviteRequested.set(true);
  }

  /** Ссылка готова: показываем её и перечитываем список выданных */
  async invitationCreated(invitation: WorkspaceInvitationModel) {
    this.createdLink.set(this.invitationApi.link(invitation.token));
    this.invitationsResource.reload();
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
      this.membersResource.reload();

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
      this.invitationsResource.reload();
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
