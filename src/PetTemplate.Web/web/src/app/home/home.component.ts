import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { UserService } from '../shared/core/user.service';
import { AuthApiService } from '../shared/api/auth-api.service';
import { I18nService } from '../shared/i18n/i18n.service';
import { PermissionsService } from '../shared/services/permissions.service';
import { PermissionEnum } from '../shared/enums/permission.enum';
import { HomeAssistantInputComponent } from './assistant-input/assistant-input.component';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrl: './home.component.css',
  imports: [RouterLink, HomeAssistantInputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class HomeComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly userService = inject(UserService);
  private readonly permissions = inject(PermissionsService);
  private readonly authService = inject(AuthApiService);
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
    await this.authService.logout();
    await this.router.navigateByUrl('/auth/login');
  }
}
