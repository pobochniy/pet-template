import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { NavigationEnd, Router, provideRouter } from '@angular/router';
import { HttpTestingController } from '@angular/common/http/testing';
import { filter, firstValueFrom } from 'rxjs';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { I18nService } from './shared/i18n/i18n.service';
import { serviceCollection } from './app.providers';
import { UserService } from './shared/core/user.service';
import { PermissionEnum } from './shared/enums/permission.enum';
import { recheckRouteAccess } from './route-access';
import { profileDto, sessionDto } from './shared/api/dto.mock';

/**
 * Словарь раздела приезжает вместе с его чанком (`withScope` в `app.routes.ts`).
 * В остальных спеках маршрута нет и словари грузит `loadAllScopes` — здесь проверяется
 * именно маршрут: без `withScope` экран входа показал бы обрубки ключей
 */
describe('app routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        ...serviceCollection
      ]
    });
  });

  it('should load the section dictionary together with its chunk', async () => {
    const i18n = TestBed.inject(I18nService);
    expect(i18n.translateCode('auth.error.invalidCredentials')).toBeNull();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/auth/login');

    expect(i18n.translateCode('auth.error.invalidCredentials')).toBe('Неверный логин или пароль');
  });

  it('should not load dictionaries of other sections', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/auth/login');

    const i18n = TestBed.inject(I18nService);
    expect(i18n.translateCode('workspace.permissions.saved')).toBeNull();
  });

  // Гость в настройки пространств не попадает — его уводят на вход
  it('should send a guest from workspace settings to the login screen', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/workspace/settings');

    expect(TestBed.inject(Router).url).toBe('/auth/login');
  });

  // Старый адрес экрана прав ведёт на участников, а без прав в пространстве туда не пускают
  it('should not let into members without a members permission', async () => {
    TestBed.inject(UserService).applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceInfoCrud] }
    }));

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/rolemanagement');

    expect(TestBed.inject(Router).url).not.toBe('/workspace/members');
  });

  it('should load workspace and invite dictionaries with the workspace section', async () => {
    TestBed.inject(UserService).applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceMemberRead] }
    }));

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/workspace/members');

    const i18n = TestBed.inject(I18nService);
    expect(i18n.translateCode('workspace.error.notAMember')).toBe('Вы не участник этого пространства');
    expect(i18n.translateCode('invite.error.cannotRevokeAccepted')).toBe('Принятое приглашение отозвать нельзя');
  });

  // Чат ассистента — ленивый экран со своим словарём (спец. assistant, 2 и 14)
  it('should load the assistant dictionary with the chat screen', async () => {
    TestBed.inject(UserService).applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [] }
    }));
    const i18n = TestBed.inject(I18nService);
    expect(i18n.translateCode('assistant.error.modelUnavailable')).toBeNull();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/assistant');

    expect(i18n.translateCode('assistant.error.modelUnavailable')).toBe('Ассистент сейчас недоступен, попробуйте позже');
  });

  it('should send a guest from the assistant to the login screen', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/assistant');

    expect(TestBed.inject(Router).url).toBe('/auth/login');
  });

  // Память ассистента — в профиле, но со словарём ассистента (спец. assistant, 12.5)
  it('should load the assistant dictionary with the assistant memory screen', async () => {
    TestBed.inject(UserService).applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [] }
    }));
    const i18n = TestBed.inject(I18nService);
    expect(i18n.translateCode('assistant.error.transcriptionFailed')).toBeNull();

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/profile/assistant-memory');

    expect(i18n.translateCode('assistant.error.transcriptionFailed')).toBe('Не удалось распознать запись');
  });

  // Справка — гостю тоже, со своим словарём (спец. help, 6.5)
  it('should open help for a guest with the help dictionary', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/help');

    expect(TestBed.inject(Router).url).toBe('/help');
    expect(harness.routeNativeElement?.textContent).toContain('Справка');
    TestBed.inject(HttpTestingController).expectOne('/api/Help').flush([]);
  });

  // Гард проверяется и на открытом экране: сменили пространство в шапке — права другие
  it('should leave the members page when the switched workspace does not allow it', async () => {
    const user = TestBed.inject(UserService);
    user.applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceMemberRead], 'ws-2': [PermissionEnum.WorkspaceInfoCrud] }
    }), 'ws-1');
    TestBed.runInInjectionContext(() => recheckRouteAccess());

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/workspace/members');
    const router = TestBed.inject(Router);
    expect(router.url).toBe('/workspace/members');

    user.switchWorkspace('ws-2');
    TestBed.tick();
    await harness.fixture.whenStable();

    expect(router.url).toBe('/');
  });

  // Право отозвали чужими руками, клиент перечитал набор — открытый экран проходит гард заново
  it('should leave the members page when refreshed permissions no longer allow it', async () => {
    const user = TestBed.inject(UserService);
    user.applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: '1', userName: 'Masha' }),
      permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceMemberRead] }
    }), 'ws-1');
    TestBed.runInInjectionContext(() => recheckRouteAccess());

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/workspace/members');
    const router = TestBed.inject(Router);
    expect(router.url).toBe('/workspace/members');

    user.applyPermissions({ 'ws-1': [PermissionEnum.WorkspaceInfoCrud] }, '1');
    TestBed.tick();
    await harness.fixture.whenStable();

    expect(router.url).toBe('/');
  });

  it('should stay on the members page when the switched workspace allows it', async () => {
    const user = TestBed.inject(UserService);
    user.applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: '1', userName: 'Masha', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceMemberRead], 'ws-2': [PermissionEnum.PermissionManagement] }
    }), 'ws-1');
    TestBed.runInInjectionContext(() => recheckRouteAccess());

    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/workspace/members');
    const router = TestBed.inject(Router);
    const rechecked = firstValueFrom(router.events.pipe(filter(event => event instanceof NavigationEnd)));

    user.switchWorkspace('ws-2');
    TestBed.tick();
    await rechecked;

    expect(router.url).toBe('/workspace/members');
    // Экран остался и перечитывает состав — запросы закрываем, чтобы не висели
    TestBed.inject(HttpTestingController).match(() => true)
      .filter(request => !request.cancelled)
      .forEach(request => request.flush([]));
  });
});
