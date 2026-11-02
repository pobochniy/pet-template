import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { UsersApiService } from '../../../shared/api/users-api.service';
import { WorkspaceContextService } from '../../../shared/core/workspace-context.service';
import { PermissionsService } from '../../../shared/services/permissions.service';
import { AlertsService } from '../../../shared/alerts/alerts.service';
import { serverErrorMessage, serverFieldErrors } from '../../../shared/core/server-error.util';
import { PermissionEnum } from '../../../shared/enums/permission.enum';
import {
  PERMISSION_CATALOG,
  PermissionInfo,
  groupPermissions
} from '../../../shared/permissions/permission-catalog';
import { I18nService } from '../../../shared/i18n/i18n.service';

/**
 * Права одного участника (спец. authorization, 5 и 7.3, право `PermissionManagement`).
 * Вырос из экрана `role-management` шаблона: тот же набор галок по группам, но
 * в строке списка участников и внутри активного пространства (как ASU
 * `role-management`, только без выбора организации — она из контекста).
 *
 * Компонент создаётся заново на каждое раскрытие, поэтому права подтягивает
 * в `ngOnInit` — вход `userId` по ходу жизни не меняется.
 *
 * В галках только те права, что есть у самого правящего, — как и в форме приглашения:
 * выдать больше, чем есть, сервер не даст (400). Права, которые участнику уже выдал
 * кто-то более правый, показываем тоже: иначе сохранение молча снимало бы их
 */
@Component({
  selector: 'workspace-member-permissions',
  templateUrl: './member-permissions.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkspaceMemberPermissionsComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly usersApi = inject(UsersApiService);
  private readonly workspaceContext = inject(WorkspaceContextService);
  private readonly permissionsService = inject(PermissionsService);
  private readonly alerts = inject(AlertsService);

  readonly userId = input.required<string>();

  /** Сохранили — родителю пора закрыть редактор */
  readonly saved = output<void>();
  readonly closed = output<void>();

  readonly checked = signal<ReadonlySet<PermissionEnum>>(new Set());
  readonly busy = signal(false);

  /** Отказ сервера у поля прав (422): показываем под галками, а не плашкой */
  readonly fieldErrors = signal<string[]>([]);

  /**
   * Права участника на момент загрузки. Показываем и те, которых у правящего
   * нет: снятая галка — это снятие права, и прятать такое от глаз нельзя
   */
  private readonly granted = signal<ReadonlySet<PermissionEnum>>(new Set());

  readonly groups = computed(() =>
    groupPermissions(PERMISSION_CATALOG.filter(
      x => this.permissionsService.hasPermission(x.permission) || this.granted().has(x.permission)
    ))
  );

  /**
   * Хотя бы одно право обязано остаться: членство в пространстве — это и есть строки
   * прав, и пустой набор выкинул бы человека из пространства. Убирают участника
   * кнопкой «Выгнать», а не снятием последней галки; сервер такой набор тоже не примет
   */
  readonly hasChecked = computed(() => this.checked().size > 0);

  async ngOnInit() {
    await this.load();
  }

  isChecked(permission: PermissionEnum): boolean {
    return this.checked().has(permission);
  }

  toggle(permission: PermissionEnum) {
    this.fieldErrors.set([]);
    this.checked.update(current => {
      const next = new Set(current);

      if (!next.delete(permission)) next.add(permission);

      return next;
    });
  }

  /** Отметить всю группу разом, а если она уже отмечена целиком — снять */
  toggleGroup(permissions: readonly PermissionInfo[]) {
    this.fieldErrors.set([]);
    this.checked.update(current => {
      const next = new Set(current);
      const allChecked = permissions.every(x => next.has(x.permission));

      for (const { permission } of permissions) {
        if (allChecked) next.delete(permission);
        else next.add(permission);
      }

      return next;
    });
  }

  async save() {
    if (!this.hasChecked()) return;

    this.busy.set(true);
    try {
      await this.usersApi.setUserRoles(this.userId(), this.workspaceId(), [...this.checked()]);

      this.alerts.push('success', this.i18n.t('workspace.permissions.saved'), 3000);
      this.saved.emit();
    } catch (error: unknown) {
      // Пустой набор — 422 у поля прав; остальные отказы смотрят в базу (400) —
      // их текст сервер отдаёт в теле ответа
      const atField = serverFieldErrors(error, 'permissions', this.i18n);
      if (atField.length > 0) {
        this.fieldErrors.set(atField);
        return;
      }

      this.alerts.push('danger',
        serverErrorMessage(error, this.i18n, this.i18n.t('workspace.error.savePermissionsFailed')),
        5000);
    } finally {
      this.busy.set(false);
    }
  }

  private async load() {
    try {
      const result = await this.usersApi.getUserRoles(this.userId(), this.workspaceId());
      this.granted.set(new Set(result));
      this.checked.set(new Set(result));
    } catch {
      this.alerts.push('danger', this.i18n.t('workspace.permissions.loadFailed'), 5000);
    }
  }

  private workspaceId(): string {
    return this.workspaceContext.getCurrentWorkspaceId() ?? '';
  }
}
