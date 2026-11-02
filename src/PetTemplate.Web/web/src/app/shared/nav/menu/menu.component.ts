import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CircleUser, House, LucideAngularModule, Users } from 'lucide-angular';
import { UserService } from '../../core/user.service';
import { PermissionsService } from '../../services/permissions.service';
import { MEMBERS_PAGE_PERMISSIONS } from '../../../workspace/workspace-permissions';
import { EventEmitterService } from '../event-emitter.service';
import { I18nService } from '../../i18n/i18n.service';
import { provideIcons } from '../../icons/lucide-icons.provider';

@Component({
  selector: 'shared-nav-menu',
  templateUrl: './menu.component.html',
  styleUrls: ['./menu.component.css'],
  imports: [RouterLink, RouterLinkActive, LucideAngularModule],
  providers: [provideIcons({ CircleUser, House, Users })],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NavMenuComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionsService);

  readonly isExpanded = signal(false);

  /**
   * Пункт «Участники» — тем, кого пустит маршрут: по любому из прав страницы
   * в активном пространстве (спец. authorization, 7.3)
   */
  protected readonly canSeeMembers = computed(() =>
    MEMBERS_PAGE_PERMISSIONS.some(x => this.permissions.permissions().includes(x))
  );

  private readonly destroyRef = inject(DestroyRef);

  constructor(
    private eventEmitterService: EventEmitterService,
    public userService: UserService
  ) { }

  ngOnInit(): void {
    this.eventEmitterService.invokeMenuToggleMenuFunction
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.toggleMenu());
  }

  toggleMenu() {
    this.isExpanded.update(value => !value);
  }

  /**
   * В мобильном виде меню выезжает поверх страницы, и после перехода по пункту
   * оно закрывает собой то, куда человек только что перешёл, — сворачиваем.
   * На широком экране меню стоит сбоку и ничего не перекрывает
   */
  onItemClick() {
    if (window.matchMedia('(max-width: 768px)').matches) this.isExpanded.set(false);
  }
}
