import { ChangeDetectionStrategy, Component, DestroyRef, Injector, computed, effect, inject, signal } from '@angular/core';
import LogOut from 'lucide/dist/esm/icons/log-out.mjs';
import Menu from 'lucide/dist/esm/icons/menu.mjs';
import Settings from 'lucide/dist/esm/icons/settings.mjs';
import User from 'lucide/dist/esm/icons/user.mjs';
import { Router } from '@angular/router';
import { MenuStateService } from '../menu-state.service';
import { UserService } from '../../core/user.service';
import { WorkspaceContextService } from '../../core/workspace-context.service';
import { WorkspaceModel } from '../../models/workspace.model';
import { Lang, langMeta } from '../../i18n/lang';
import { LangFlagComponent } from '../../i18n/flags/lang-flag.component';
import { LanguagePickerComponent } from '../../language-picker/language-picker.component';
import { IconComponent } from '../../icons/icon.component';
import { injectI18n } from '../../i18n/inject-i18n';

@Component({
  selector: 'top-nav',
  templateUrl: './top-nav.component.html',
  styleUrls: ['./top-nav.component.css'],
  imports: [IconComponent, LangFlagComponent, LanguagePickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TopNavComponent {
  protected readonly icons = { LogOut, Menu, Settings, User };

  protected readonly i18n = injectI18n();

  protected readonly workspaceContext = inject(WorkspaceContextService);
  private readonly injector = inject(Injector);
  private destroyed = false;

  readonly isLanguagePickerOpen = signal(false);
  readonly currentLanguage = computed(() => langMeta(this.i18n.lang()));

  /** Свои пространства для селектора (спец. authorization, 7) — только те, что есть в сессии */
  readonly workspaces = signal<WorkspaceModel[]>([]);

  constructor(
    private menu: MenuStateService,
    public userService: UserService,
    private router: Router
  ) {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));

    // Шапка смонтирована до входа, поэтому список грузится не один раз при старте,
    // а за самой сессией: вход, выход, созданное и покинутое пространство меняют
    // права по пространствам — по ним и перечитываем. Пространств нет — нет и запроса
    effect(() => {
      const ids = this.userService.workspaceIds();

      if (ids.length === 0) this.workspaces.set([]);
      else void this.loadWorkspaces(ids);
    });
  }

  /**
   * Переключение активного пространства: весь контекст — запросы и права —
   * меняется на выбранное, основное при этом не трогается (спец. authorization, 7)
   */
  onWorkspaceChange(event: Event) {
    const value = (event.target as HTMLSelectElement).value;
    if (value) this.userService.switchWorkspace(value);
  }

  /** Настройки пространств — и тем, у кого их ещё нет: оттуда создают первое */
  async goToWorkspaceSettings() {
    await this.router.navigateByUrl('/workspace/settings');
  }

  /**
   * Сверка с сессией, а не только ответ сервера: строка списка без прав в сессии
   * переключила бы на пространство, в котором токен ничего не может.
   *
   * `WorkspaceApiService` — через `import()`: списку пространств нужен вошедший, а гостю
   * сервис в initial-бандле ни к чему (`tech-debt/bundle.md`)
   */
  private async loadWorkspaces(ids: readonly string[]) {
    try {
      const { WorkspaceApiService } = await import('../../api/workspace-api.service');
      // Пока чанк ехал, шапку могли убрать — её инжектор уже не отдаст сервис
      if (this.destroyed) return;
      const list = await this.injector.get(WorkspaceApiService).getList();
      this.workspaces.set(list.filter(x => ids.includes(x.id)));
    } catch (error) {
      console.error('Failed to load workspaces', error);
    }
  }

  toggleMenu() {
    this.menu.toggle();
  }

  /** Состояние разделов и саму сессию убирает `UserService.logOut` (`SessionResetService`) */
  async logOut() {
    await this.userService.logOut();
    await this.router.navigateByUrl('/');
  }

  async goToProfile() {
    await this.router.navigateByUrl('/profile');
  }

  toggleLanguagePicker() {
    this.isLanguagePickerOpen.update(open => !open);
  }

  closeLanguagePicker() {
    this.isLanguagePickerOpen.set(false);
  }

  /**
   * Язык меняется сразу: словарь `core` лежит в бандле, выбор — в `localStorage`.
   * У вошедшего он следом и молча уезжает в профиль (`LanguageService`, спец. accounts,
   * 8.4). Сервис грузится по месту: с ним в initial ехал бы `AccountApiService`, а нужен
   * он только в момент смены языка
   */
  async onLanguagePicked(lang: Lang) {
    this.closeLanguagePicker();
    await this.i18n.setLang(lang);
    if (!this.userService.isAuthenticated) return;

    try {
      const { LanguageService } = await import('../../i18n/language.service');
      if (this.destroyed) return;
      await this.injector.get(LanguageService).persist(lang);
    } catch (error) {
      // Молча, как и само сохранение: чанк не приехал
      console.warn('[i18n] language was not saved to the profile', error);
    }
  }
}
