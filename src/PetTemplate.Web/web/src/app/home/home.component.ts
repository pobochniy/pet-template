import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { UserService } from '../shared/core/user.service';
import { PermissionsService } from '../shared/services/permissions.service';
import { PermissionEnum } from '../shared/enums/permission.enum';
import { HomeAssistantInputComponent } from './assistant-input/assistant-input.component';
import { injectI18n } from '../shared/i18n/inject-i18n';
import { HOME_SCOPES } from './home.scopes';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrl: './home.component.css',
  imports: [RouterLink, HomeAssistantInputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class HomeComponent {
  protected readonly i18n = injectI18n(...HOME_SCOPES);
  protected readonly userService = inject(UserService);
  private readonly permissions = inject(PermissionsService);
  private readonly router = inject(Router);

  /**
   * Пространств нет — пропустил шаг при регистрации или ушёл из всех: одна плашка
   * с кнопкой на создание (спец. authorization, 3, п. 3)
   */
  protected readonly noWorkspace = computed(() => this.userService.workspaceIds().length === 0);

  /**
   * Пространство одно — зовём остальных. Только тем, у кого есть право выдать ссылку:
   * без него на странице участников формы приглашения нет, и кнопка обещала бы не то
   */
  protected readonly canInvite = computed(() =>
    this.userService.workspaceIds().length === 1
    && this.permissions.permissions().includes(PermissionEnum.WorkspaceMemberCrud));

  async logout() {
    await this.userService.logOut();
    await this.router.navigateByUrl('/auth/login');
  }
}
