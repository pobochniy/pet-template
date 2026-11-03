import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CircleQuestionMark, CircleUser, House, LucideAngularModule, Users } from 'lucide-angular';
import { UserService } from '../../core/user.service';
import { PermissionsService } from '../../services/permissions.service';
import { MEMBERS_PAGE_PERMISSIONS } from '../../../workspace/workspace-permissions';
import { MenuStateService } from '../menu-state.service';
import { provideIcons } from '../../icons/lucide-icons.provider';
import { injectI18n } from '../../i18n/inject-i18n';

@Component({
  selector: 'shared-nav-menu',
  templateUrl: './menu.component.html',
  styleUrls: ['./menu.component.css'],
  imports: [RouterLink, RouterLinkActive, LucideAngularModule],
  providers: [provideIcons({ CircleQuestionMark, CircleUser, House, Users })],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NavMenuComponent {
  protected readonly i18n = injectI18n();
  private readonly permissions = inject(PermissionsService);

  private readonly menu = inject(MenuStateService);

  readonly isExpanded = this.menu.expanded;

  /**
   * Пункт «Участники» — тем, кого пустит маршрут: по любому из прав страницы
   * в активном пространстве (спец. authorization, 7.3)
   */
  protected readonly canSeeMembers = computed(() =>
    MEMBERS_PAGE_PERMISSIONS.some(x => this.permissions.permissions().includes(x))
  );

  constructor(public userService: UserService) { }

  toggleMenu() {
    this.menu.toggle();
  }

  /**
   * В мобильном виде меню выезжает поверх страницы, и после перехода по пункту
   * оно закрывает собой то, куда человек только что перешёл, — сворачиваем.
   * На широком экране меню стоит сбоку и ничего не перекрывает
   */
  onItemClick() {
    if (window.matchMedia('(max-width: 768px)').matches) this.menu.collapse();
  }
}
