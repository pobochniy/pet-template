import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PermissionsRefreshService } from './permissions-refresh.service';
import { UserService } from './user.service';
import { PermissionsService } from '../services/permissions.service';
import { WorkspaceContextService } from './workspace-context.service';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { PermissionEnum } from '../enums/permission.enum';
import { profileDto, sessionDto } from '../api/dto.mock';

/**
 * Права, поменянные чужими руками (спец. authorization, 2.2): клиент перечитывает свой
 * набор на `403` и на возврат во вкладку, не чаще порога, и раскладывает его в сессию
 */
describe('PermissionsRefreshService', () => {
  const URL = '/api/Workspace/MyPermissions';
  const profile = profileDto({ id: 'me', userName: 'Oleg' });

  let service: PermissionsRefreshService;
  let user: UserService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(PermissionsRefreshService);
    user = TestBed.inject(UserService);
    httpMock = TestBed.inject(HttpTestingController);
    user.applySession(sessionDto({
      token: 'token-1',
      profile,
      permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceMemberRead], 'ws-2': [] }
    }), 'ws-1');
  });

  afterEach(() => httpMock.verify());

  /** Запрос уходит после `import()` api-сервиса — ждём его появления */
  async function nextRequest() {
    for (let i = 0; i < 20; i++) {
      const found = httpMock.match(URL);
      if (found.length > 0) return found[0];
      await new Promise(resolve => setTimeout(resolve));
    }
    return httpMock.expectOne(URL);
  }

  it('should apply a set changed by someone else to the menu permissions', async () => {
    const done = service.refresh('forbidden');
    (await nextRequest()).flush({ 'ws-1': [PermissionEnum.WorkspaceMemberRead, PermissionEnum.PermissionManagement], 'ws-2': [] });
    await done;

    expect(TestBed.inject(PermissionsService).hasPermission(PermissionEnum.PermissionManagement)).toBe(true);
    expect(user.permissionsIn('ws-1')).toContain(PermissionEnum.PermissionManagement);
  });

  it('should move off the workspace the person was removed from', async () => {
    const done = service.refresh('forbidden');
    (await nextRequest()).flush({ 'ws-2': [PermissionEnum.WorkspaceInfoCrud] });
    await done;

    expect(user.workspaceIds()).toEqual(['ws-2']);
    expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-2');
    expect(TestBed.inject(PermissionsService).permissions()).toEqual([PermissionEnum.WorkspaceInfoCrud]);
  });

  it('should not ask again on focus within the interval', async () => {
    const done = service.refresh('focus');
    (await nextRequest()).flush({ 'ws-1': [PermissionEnum.WorkspaceMemberRead], 'ws-2': [] });
    await done;

    await service.refresh('focus');

    expect(httpMock.match(URL)).toEqual([]);
  });

  it('should send one request for several refusals at once', async () => {
    const first = service.refresh('forbidden');
    const second = service.refresh('forbidden');
    (await nextRequest()).flush({ 'ws-1': [], 'ws-2': [] });
    await Promise.all([first, second]);

    expect(httpMock.match(URL)).toEqual([]);
  });

  it('should not ask for a guest', async () => {
    user.clearProfile();

    await service.refresh('forbidden');

    expect(httpMock.match(URL)).toEqual([]);
  });

  // 403 любого своего запроса — повод перечитать права; 403 самих «моих прав» — нет
  it('should refresh on a refusal of the own API', async () => {
    const refused = firstValueFrom(TestBed.inject(HttpClient).get('/api/Users/GetList')).catch(() => null);
    httpMock.expectOne('/api/Users/GetList').flush(null, { status: 403, statusText: 'Forbidden' });
    await refused;

    const req = await nextRequest();
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-1');
    req.flush(null, { status: 403, statusText: 'Forbidden' });
    await new Promise(resolve => setTimeout(resolve));

    expect(httpMock.match(URL)).toEqual([]);
  });

  it('should refresh when the tab becomes visible again', async () => {
    TestBed.runInInjectionContext(() => service.watch());

    window.dispatchEvent(new Event('focus'));

    const req = await nextRequest();
    expect(req.request.method).toBe('GET');
    req.flush({ 'ws-1': [PermissionEnum.WorkspaceMemberRead], 'ws-2': [] });
  });
});
