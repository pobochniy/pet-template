import { ChangeDetectionStrategy, Component, effect, inject, output, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { InvitationApiService } from '../../../shared/api/invitation-api.service';
import { WorkspaceContextService } from '../../../shared/core/workspace-context.service';
import { PermissionsService } from '../../../shared/services/permissions.service';
import { AlertsService } from '../../../shared/alerts/alerts.service';
import { serverErrorMessage, serverFieldErrors } from '../../../shared/core/server-error.util';
import { WorkspaceInvitationModel } from '../../../shared/models/workspace-invitation.model';
import { PERMISSION_CATALOG, PermissionInfo } from '../../../shared/permissions/permission-catalog';
import { PermissionEnum } from '../../../shared/enums/permission.enum';
import {
  createInvitationForm,
  selectedPermissions,
  syncInvitationPermissions
} from '../../../shared/form-models/invitation-form.model';
import { FormValidationComponent } from '../../../shared/form-validation/form-validation.component';
import { I18nService } from '../../../shared/i18n/i18n.service';

/**
 * Форма выдачи ссылки-приглашения (спец. authorization, 6.1).
 *
 * В форме только те права, что есть у самого пользователя: выдать больше, чем есть,
 * сервер всё равно не даст (400), и предлагать их значило бы обещать невозможное.
 * Набор идёт за правами: они меняются под открытой страницей — например при смене
 * пространства, — и форма пересобирает разницу, не теряя снятых галочек и имени.
 *
 * Выданную ссылку показывает родитель: он же держит список выданных, и обе половины
 * одного действия живут рядом
 */
@Component({
  selector: 'workspace-invite-form',
  templateUrl: './invite-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, FormValidationComponent]
})
export class WorkspaceInviteFormComponent {
  protected readonly i18n = inject(I18nService);
  private readonly invitationApi = inject(InvitationApiService);
  private readonly workspaceContext = inject(WorkspaceContextService);
  private readonly permissionsService = inject(PermissionsService);
  private readonly alerts = inject(AlertsService);

  readonly created = output<WorkspaceInvitationModel>();

  readonly busy = signal(false);

  /** Отказ сервера у поля прав (422) */
  readonly fieldErrors = signal<string[]>([]);

  /** Права, доступные к выдаче: подписи общие с правкой прав участника */
  readonly availablePermissions = signal<readonly PermissionInfo[]>([]);

  readonly form = createInvitationForm([]);

  constructor() {
    // Список и чекбоксы правит один метод: шаблон рисует `formControlName` по этому
    // списку, и права, которого ещё нет в форме, в нём быть не должно
    effect(() => this.syncAvailablePermissions(this.permissionsService.permissions()));
    this.syncAvailablePermissions(this.permissionsService.permissions());
  }

  private syncAvailablePermissions(granted: readonly PermissionEnum[]): void {
    const available = PERMISSION_CATALOG.filter(x => granted.includes(x.permission));

    syncInvitationPermissions(this.form.controls.permissions, available.map(x => x.permission));
    this.availablePermissions.set(available);
  }

  async create() {
    const workspaceId = this.workspaceContext.getCurrentWorkspaceId();
    // Пометить поля обязательно: `form-validation` молчит, пока поле не тронули
    this.form.markAllAsTouched();
    if (!this.form.valid || !workspaceId) return;

    this.busy.set(true);
    this.fieldErrors.set([]);
    try {
      const created = await this.invitationApi.create({
        workspaceId,
        inviteeName: this.form.controls.inviteeName.value.trim() || null,
        permissions: selectedPermissions(this.form.controls.permissions)
      });

      // Имя чистим, права оставляем: следующего чаще звать с тем же набором,
      // а вот прежнее имя в поле — верный способ выписать ссылку не тому
      this.form.controls.inviteeName.reset('');

      this.created.emit(created);
    } catch (error: unknown) {
      const atField = serverFieldErrors(error, 'permissions', this.i18n);
      if (atField.length > 0) {
        this.fieldErrors.set(atField);
        return;
      }

      // «Нельзя выдать права, которых нет у вас самих» и остальные отказы —
      // из тела ответа: своя формулировка на клиенте разъехалась бы с серверной
      this.alerts.push('danger', serverErrorMessage(error, this.i18n, this.i18n.t('workspace.error.inviteFailed')), 5000);
    } finally {
      this.busy.set(false);
    }
  }
}
