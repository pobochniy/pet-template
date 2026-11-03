import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { UserService } from '../core/user.service';
import { AlertsService } from '../alerts/alerts.service';
import { SESSION_KEY } from '../core/session-store';
import { profileDto, sessionDto } from '../api/dto.mock';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let user: UserService;
  let router: Router;

  const profile = profileDto({ id: 'me', userName: 'Oleg', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    user = TestBed.inject(UserService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    user.applySession(sessionDto({ token: 'token-1', profile, permissionsByWorkspace: {} }));
  });

  afterEach(() => httpMock.verify());

  async function unauthorized() {
    const request = firstValueFrom(http.get('/api/Profile/Details')).catch(() => null);
    const req = httpMock.expectOne('/api/Profile/Details');
    return { req, done: () => (req.flush(null, { status: 401, statusText: 'Unauthorized' }), request) };
  }

  it('should end the session when the server refuses the stored token', async () => {
    const { req, done } = await unauthorized();
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-1');

    await done();

    expect(user.isAuthenticated).toBe(false);
    expect(localStorage.getItem(SESSION_KEY)).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/auth/login');
  });

  // Пока запрос со старым токеном шёл, соседняя вкладка вошла заново: её вход не стирается
  it('should keep a fresh session written by another tab while the request was in flight', async () => {
    const alerts = TestBed.inject(AlertsService);
    const { done } = await unauthorized();
    localStorage.setItem(SESSION_KEY, JSON.stringify(sessionDto({ token: 'token-2', profile, permissionsByWorkspace: {} })));

    await done();

    expect(user.isAuthenticated).toBe(true);
    expect(JSON.parse(localStorage.getItem(SESSION_KEY)!).token).toBe('token-2');
    expect(alerts.alerts()).toEqual([]);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
