import { ChangeDetectionStrategy, Component, DestroyRef, Injector, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { LogOut, LucideAngularModule, Menu, Settings, User } from 'lucide-angular';
import { EventEmitterService } from '../event-emitter.service';
import { UserService } from '../../core/user.service';
import { WorkspaceContextService } from '../../core/workspace-context.service';
import { WorkspaceModel } from '../../models/workspace.model';
import { I18nService } from '../../i18n/i18n.service';
import { Lang, langMeta } from '../../i18n/lang';
import { LangFlagComponent } from '../../i18n/flags/lang-flag.component';
import { LanguagePickerComponent } from '../../language-picker/language-picker.component';
import { provideIcons } from '../../icons/lucide-icons.provider';

@Component({
  selector: 'top-nav',
  templateUrl: './top-nav.component.html',
  styleUrls: ['./top-nav.component.css'],
  imports: [LucideAngularModule, LangFlagComponent, LanguagePickerComponent],
  providers: [provideIcons({ LogOut, Menu, Settings, User })],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TopNavComponent {
  protected readonly i18n = inject(I18nService);

  protected readonly workspaceContext = inject(WorkspaceContextService);
  private readonly injector = inject(Injector);
  private destroyed = false;

  readonly isLanguagePickerOpen = signal(false);
  readonly currentLanguage = computed(() => langMeta(this.i18n.lang()));

  /** Свои пространства для селектора (спец. authorization, 7) — только те, что есть в сессии */
  readonly workspaces = signal<WorkspaceModel[]>([]);

  constructor(
    private eventEmitterService: EventEmitterService,
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
    this.eventEmitterService.onToggleMenuButtonClick();
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
