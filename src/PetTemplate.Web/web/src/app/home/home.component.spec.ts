import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { HomeComponent } from './home.component';
import { UserService } from '../shared/core/user.service';
import { AuthApiService } from '../shared/api/auth-api.service';
import { I18nService } from '../shared/i18n/i18n.service';
import { loadAllScopes } from '../shared/i18n/i18n.testing';
import { PermissionEnum } from '../shared/enums/permission.enum';

describe('HomeComponent', () => {
  let fixture: ComponentFixture<HomeComponent>;
  let el: HTMLElement;
  let authService: jasmine.SpyObj<AuthApiService>;
  let userService: UserService;
  let router: Router;

  beforeEach(async () => {
    authService = jasmine.createSpyObj<AuthApiService>('AuthApiService', ['logout']);
    authService.logout.and.resolveTo();

    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        { provide: AuthApiService, useValue: authService }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    userService = TestBed.inject(UserService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);

    fixture = TestBed.createComponent(HomeComponent);
    el = fixture.nativeElement;
  });

  it('should offer sign-up and log-in to a guest', async () => {
    await fixture.whenStable();

    const links = Array.from(el.querySelectorAll('a')).map(a => a.textContent?.trim());
    expect(links).toEqual(['Регистрация', 'Войти']);
  });

  it('should greet a signed-in user by name', async () => {
    userService.setProfile({
      id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true
    });
    await fixture.whenStable();

    expect(el.textContent).toContain('Добро пожаловать, Masha!');
  });

  it('should log out and go to the login screen', async () => {
    userService.setProfile({
      id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true
    });
    await fixture.whenStable();

    await fixture.componentInstance.logout();

    expect(authService.logout).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/auth/login');
  });

  it('should invite a user without workspaces to create one', async () => {
    userService.setProfile({
      id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true
    });
    await fixture.whenStable();

    expect(el.querySelector('#no-workspace a')?.getAttribute('href')).toBe('/workspace/settings');
    // Ассистент живёт в пространстве — без него строке ввода вести некуда
    expect(el.querySelector('home-assistant-input')).toBeNull();
  });

  // Пространство одно и право звать есть — вместо плашки кнопка «Пригласить участников»
  it('should offer inviting members in the only workspace', async () => {
    userService.applySession({
      token: 'token',
      profile: { id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true },
      permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceMemberCrud] }
    });
    await fixture.whenStable();

    expect(el.querySelector('#no-workspace')).toBeNull();
    expect(el.querySelector('#invite-members')?.getAttribute('href')).toBe('/workspace/members');
  });

  // Строка ассистента — на главной у каждого, у кого есть пространство (спец. assistant, 2)
  it('should show the assistant line to a member of a workspace', async () => {
    userService.applySession({
      token: 'token',
      profile: { id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true },
      permissionsByWorkspace: { 'ws-1': [] }
    });
    await fixture.whenStable();

    expect(el.querySelector('#home-assistant-input')?.getAttribute('placeholder')).toBe('Спросите ассистента');
  });
});
