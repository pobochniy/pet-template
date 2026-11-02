import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router } from '@angular/router';
import { TopNavComponent } from './top-nav.component';
import { UserService } from '../../core/user.service';
import { SessionResetService } from '../../core/session-reset.service';
import { AccountApiService } from '../../api/account-api.service';
import { AccountApiServiceMock } from '../../api/account-api.service.mock';
import { I18nService } from '../../i18n/i18n.service';
import { EventEmitterService } from '../event-emitter.service';
import { WorkspaceApiService } from '../../api/workspace-api.service';
import { WorkspaceContextService } from '../../core/workspace-context.service';
import { PermissionsService } from '../../services/permissions.service';
import { PermissionEnum } from '../../enums/permission.enum';
import { WorkspaceModel } from '../../models/workspace.model';
import { ProfileModel } from '../../models/profile.model';

describe('TopNavComponent', () => {
  let fixture: ComponentFixture<TopNavComponent>;
  let el: HTMLElement;
  let userService: UserService;
  let router: jasmine.SpyObj<Router>;
  let workspaceApi: jasmine.SpyObj<WorkspaceApiService>;

  const profile: ProfileModel = {
    id: '1', userName: 'user', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true,
    primaryWorkspaceId: 'ws-1'
  };

  const workspaces: WorkspaceModel[] = [
    { id: 'ws-1', name: 'Рога и Копыта' },
    { id: 'ws-2', name: 'Пончики и Плюшки' }
  ];

  async function render(authenticated: boolean): Promise<void> {
    router = jasmine.createSpyObj<Router>('Router', ['navigateByUrl']);
    router.navigateByUrl.and.resolveTo(true);
    workspaceApi = jasmine.createSpyObj<WorkspaceApiService>('WorkspaceApiService', ['getList']);
    workspaceApi.getList.and.resolveTo([]);

    await TestBed.configureTestingModule({
      imports: [TopNavComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: AccountApiService, useClass: AccountApiServiceMock },
        { provide: AccountApiServiceMock, useExisting: AccountApiService },
        { provide: Router, useValue: router },
        { provide: WorkspaceApiService, useValue: workspaceApi }
      ]
    }).compileComponents();

    userService = TestBed.inject(UserService);
    if (authenticated) {
      userService.setProfile({
        id: '1', userName: 'user', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true
      });
    }

    fixture = TestBed.createComponent(TopNavComponent);
    el = fixture.nativeElement;
    await fixture.whenStable();
  }

  const button = (label: string) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find(b => b.getAttribute('aria-label') === label);

  it('should show neither profile nor log out to a guest', async () => {
    await render(false);

    expect(button('Профиль')).toBeUndefined();
    expect(button('Выйти')).toBeUndefined();
  });

  it('should show profile and log out to a signed-in user', async () => {
    await render(true);

    expect(button('Профиль')).toBeDefined();
    expect(button('Выйти')).toBeDefined();
  });

  it('should clean up the session and go home on log out', async () => {
    await render(true);
    const cleanup = jasmine.createSpy('cleanup');
    TestBed.inject(SessionResetService).register(cleanup);

    button('Выйти')!.click();
    await fixture.whenStable();

    expect(cleanup).toHaveBeenCalled();
    expect(userService.isAuthenticated).toBeFalse();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  /**
   * Список пространств приходит после `import()` сервиса и ответа сервера — цепочкой
   * мимо `whenStable`: макрозадача дожидается её целиком
   */
  async function settle(): Promise<void> {
    // Тот же модуль, что грузит шапка: дождались его — дальше один ответ подмены
    await import('../../api/workspace-api.service');
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  /** Вошёл с двумя пространствами: список приходит после ответа сервера */
  async function renderWithWorkspaces(): Promise<void> {
    await render(false);
    workspaceApi.getList.and.resolveTo(workspaces);
    TestBed.inject(UserService).applySession({
      token: 'token',
      profile,
      permissionsByWorkspace: {
        'ws-1': [PermissionEnum.WorkspaceMemberRead],
        'ws-2': [PermissionEnum.PermissionManagement]
      }
    });
    await settle();
  }

  it('should list own workspaces with the primary one active', async () => {
    await renderWithWorkspaces();

    const select = el.querySelector<HTMLSelectElement>('#workspace-selector')!;
    expect(Array.from(select.options).map(o => o.textContent?.trim())).toEqual(['Рога и Копыта', 'Пончики и Плюшки']);
    expect(select.value).toBe('ws-1');
    expect(select.disabled).toBeFalse();
  });

  /** Пространство и права переключаются вместе: меню рисуется по правам выбранного */
  it('should switch the workspace together with its permissions', async () => {
    await renderWithWorkspaces();

    const select = el.querySelector<HTMLSelectElement>('#workspace-selector')!;
    select.value = 'ws-2';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-2');
    expect(TestBed.inject(PermissionsService).permissions()).toEqual([PermissionEnum.PermissionManagement]);
  });

  it('should disable the selector when there is a single workspace', async () => {
    await render(false);
    workspaceApi.getList.and.resolveTo([workspaces[0]]);
    TestBed.inject(UserService).applySession({
      token: 'token', profile, permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceMemberRead] }
    });
    await settle();

    expect(el.querySelector<HTMLSelectElement>('#workspace-selector')!.disabled).toBeTrue();
  });

  it('should not ask for workspaces of a user without any', async () => {
    await render(true);

    expect(workspaceApi.getList).not.toHaveBeenCalled();
    expect(el.querySelector('#workspace-selector')).toBeNull();
    expect(el.querySelector('#workspace-settings')).not.toBeNull();
  });

  it('should toggle the side menu', async () => {
    await render(false);
    const toggled = jasmine.createSpy('toggled');
    TestBed.inject(EventEmitterService).invokeMenuToggleMenuFunction.subscribe(toggled);

    button('Показать или скрыть меню')!.click();

    expect(toggled).toHaveBeenCalled();
  });

  it('should switch the interface language from the picker', async () => {
    await render(false);

    el.querySelector<HTMLButtonElement>('#language-picker-button')!.click();
    await fixture.whenStable();

    const english = Array.from(el.querySelectorAll<HTMLButtonElement>('.language-picker__item'))
      .find(item => item.textContent?.includes('English'))!;
    english.click();
    await fixture.whenStable();

    expect(TestBed.inject(I18nService).lang()).toBe('en');
    expect(el.querySelector('.language-picker')).toBeNull();
    expect(button('Show or hide the menu')).toBeDefined();
  });

  async function pickLanguage(name: string): Promise<void> {
    el.querySelector<HTMLButtonElement>('#language-picker-button')!.click();
    await fixture.whenStable();
    Array.from(el.querySelectorAll<HTMLButtonElement>('.language-picker__item'))
      .find(item => item.textContent?.includes(name))!
      .click();
  }

  /** Язык профиля — на нём сервер пишет письма (спец. accounts, 8.4) */
  it('should save the picked language to the profile', async () => {
    await render(true);
    const account = TestBed.inject(AccountApiServiceMock);

    await pickLanguage('English');
    // Сервис языка грузится по месту — ждём, пока чанк приедет и вызов уйдёт
    for (let i = 0; i < 100 && account.language == null; i++) {
      await new Promise(resolve => setTimeout(resolve, 10));
    }

    expect(account.language).toBe('en');
  });

  it('should not save the language of a guest', async () => {
    await render(false);

    await pickLanguage('English');
    await import('../../i18n/language.service');
    await new Promise(resolve => setTimeout(resolve, 50));

    expect(TestBed.inject(AccountApiServiceMock).language).toBeNull();
  });
});
