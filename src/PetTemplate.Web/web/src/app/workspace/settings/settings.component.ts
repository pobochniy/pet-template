import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { LogOut, LucideAngularModule, Users } from 'lucide-angular';
import { FolderPlus, Pencil, Star } from 'lucide-angular/src/icons';
import { WorkspaceApiService } from '../../shared/api/workspace-api.service';
import { UserService } from '../../shared/core/user.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { provideIcons } from '../../shared/icons/lucide-icons.provider';
import { WorkspaceModel } from '../../shared/models/workspace.model';
import { createWorkspaceForm } from '../../shared/form-models/workspace-form.model';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { PermissionEnum } from '../../shared/enums/permission.enum';
import { WorkspaceMembersComponent } from '../members/members.component';
import { MEMBERS_PAGE_PERMISSIONS } from '../workspace-permissions';
import { I18nService } from '../../shared/i18n/i18n.service';

/**
 * Список своих пространств, основное, переименование, создание нового и выход
 * (спец. authorization, 3, 7.2 и 8). Без проверки прав: сюда приходят и те,
 * у кого пространств ещё нет.
 *
 * При единственном пространстве экран участников показываем прямо здесь
 * (`workspace-members [embedded]`): отдельная страница под состав была бы лишним переходом
 */
@Component({
  selector: 'workspace-settings',
  templateUrl: './settings.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, FormValidationComponent, LucideAngularModule, WorkspaceMembersComponent],
  providers: [provideIcons({ FolderPlus, LogOut, Pencil, Star, Users })]
})
export class WorkspaceSettingsComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly workspaceApi = inject(WorkspaceApiService);
  private readonly userService = inject(UserService);
  private readonly workspaceContext = inject(WorkspaceContextService);
  private readonly alerts = inject(AlertsService);
  private readonly router = inject(Router);

  readonly workspaces = signal<WorkspaceModel[]>([]);
  readonly busy = signal(false);
  readonly form = createWorkspaceForm();

  /** Список приехал: до этого не показываем ни пустую подсказку, ни форму создания */
  readonly loaded = signal(false);

  /**
   * Форма нового пространства: у того, у кого пространств ещё нет, она развёрнута —
   * создать первое больше негде; у остальных свёрнута в кнопку, потому что второе
   * пространство заводят редко. Развёрнутую обратно не сворачиваем: убирать
   * из-под рук наполовину заполненную форму хуже, чем оставить её до перезагрузки
   */
  readonly createOpen = signal(false);

  /**
   * Пространство, по которому нажали «Выйти»: подтверждение спрашиваем прямо
   * в строке списка. Выход необратим — вернуться можно только по новому
   * приглашению, — а `confirm()` в браузере вешает страницу целиком
   */
  readonly leaving = signal<string | null>(null);

  /** Пространство, название которого правят, и форма правки — одна за раз */
  readonly renaming = signal<string | null>(null);
  readonly renameForm = createWorkspaceForm();

  /** Основное пространство живёт в профиле — его же меняет `SetPrimary` */
  readonly primaryWorkspaceId = computed(() => this.userService.profile()?.primaryWorkspaceId ?? null);

  /** Участники единственного пространства — прямо на этой странице */
  readonly membersInline = computed(() => {
    const list = this.workspaces();

    return list.length === 1 && this.canSeeMembers(list[0]);
  });

  async ngOnInit() {
    await this.load();
  }

  /**
   * Права в пространстве берём из сессии, а не у активного: строка списка может быть
   * и не активной, а кнопка в закрытый правом раздел вела бы на отказ
   */
  canSeeMembers(workspace: WorkspaceModel): boolean {
    const permissions = this.userService.permissionsIn(workspace.id);

    return MEMBERS_PAGE_PERMISSIONS.some(x => permissions.includes(x));
  }

  canRename(workspace: WorkspaceModel): boolean {
    return this.userService.permissionsIn(workspace.id).includes(PermissionEnum.WorkspaceInfoCrud);
  }

  /** В строке кнопка нужна, только если состав не показан ниже целиком */
  showMembersButton(workspace: WorkspaceModel): boolean {
    return !this.membersInline() && this.canSeeMembers(workspace);
  }

  /** Последнему участнику подтверждение прямо говорит, что пространство удалится */
  isLastMember(workspace: WorkspaceModel): boolean {
    return workspace.membersCount === 1;
  }

  openCreate() {
    this.createOpen.set(true);
  }

  /**
   * Участники не активного пространства: сначала переключаемся на него вместе
   * с правами, иначе страница показала бы состав предыдущего — пространство она
   * берёт из контекста
   */
  async openMembers(workspaceId: string) {
    this.userService.switchWorkspace(workspaceId);
    await this.router.navigateByUrl('/workspace/members');
  }

  startRename(workspace: WorkspaceModel) {
    this.renameForm.reset({ name: workspace.name });
    this.renaming.set(workspace.id);
  }

  cancelRename() {
    this.renaming.set(null);
  }

  /** Правка названия — с заголовком своего пространства: оно может быть не активным */
  async rename(workspaceId: string) {
    this.renameForm.markAllAsTouched();
    if (!this.renameForm.valid) return;

    this.busy.set(true);
    try {
      const name = this.renameForm.controls.name.value.trim();
      await this.workspaceApi.rename(workspaceId, name);

      this.workspaces.update(list => list.map(x => (x.id === workspaceId ? { ...x, name } : x)));
      this.renaming.set(null);
      this.alerts.push('success', this.i18n.t('workspace.settings.renamed'), 3000);
    } catch (error: unknown) {
      this.alerts.push('danger', serverErrorMessage(error, this.i18n, this.i18n.t('workspace.error.renameFailed')), 5000);
    } finally {
      this.busy.set(false);
    }
  }

  async setPrimary(workspaceId: string) {
    try {
      await this.workspaceApi.setPrimary(workspaceId);

      // Без правки профиля в сессии отметка «основное» осталась бы на прежнем
      const profile = this.userService.profile();
      if (profile) this.userService.setProfile({ ...profile, primaryWorkspaceId: workspaceId });

      this.alerts.push('success', this.i18n.t('workspace.settings.primaryChanged'), 3000);
    } catch (error: unknown) {
      this.alerts.push('danger', serverErrorMessage(error, this.i18n, this.i18n.t('workspace.error.setPrimaryFailed')), 5000);
    }
  }

  /** Первый клик по «Выйти» только спрашивает, второй — уходит */
  confirmLeave(workspaceId: string) {
    this.leaving.set(workspaceId);
  }

  cancelLeave() {
    this.leaving.set(null);
  }

  /**
   * Выход из пространства (спец. authorization, 8). Ответ — новая сессия уже без прав
   * покинутого пространства, и раскладывает её тот же код, что и ответ входа.
   * Активным остаётся прежнее, если ушли не из него
   */
  async leave(workspaceId: string) {
    this.busy.set(true);
    try {
      const active = this.workspaceContext.getCurrentWorkspaceId();
      const result = await this.workspaceApi.leave(workspaceId);
      this.userService.applySession(result, active === workspaceId ? null : active);

      this.leaving.set(null);
      await this.load();

      this.alerts.push('success', this.i18n.t('workspace.settings.left'), 3000);
    } catch (error: unknown) {
      this.alerts.push('danger', serverErrorMessage(error, this.i18n, this.i18n.t('workspace.error.leaveFailed')), 5000);
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Создание — с переходом на страницу участников нового пространства: заведя его,
   * человек чаще всего идёт звать остальных (спец. authorization, 3, п. 5).
   * Активным становится созданное, а не основное
   */
  async create() {
    this.form.markAllAsTouched();
    if (!this.form.valid) return;

    this.busy.set(true);
    try {
      const created = await this.workspaceApi.create(this.form.controls.name.value.trim());

      // Новая сессия приезжает тем же ответом: права нового пространства появились
      // в базе уже после того, как текущий токен подписан (спец. authorization, 2.2)
      this.userService.applySession(created.authentication, created.workspaceId);

      await this.router.navigateByUrl('/workspace/members');
    } catch (error: unknown) {
      this.alerts.push('danger', serverErrorMessage(error, this.i18n, this.i18n.t('workspace.error.createFailed')), 5000);
    } finally {
      this.busy.set(false);
    }
  }

  private async load() {
    try {
      const list = await this.workspaceApi.getList();
      this.workspaces.set(list);

      // Только разворачиваем: свернуть открытую форму перезагрузка списка не должна
      if (list.length === 0) this.createOpen.set(true);
    } catch {
      this.alerts.push('danger', this.i18n.t('workspace.error.loadFailed'), 5000);
    } finally {
      this.loaded.set(true);
    }
  }
}
