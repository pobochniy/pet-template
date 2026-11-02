import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { adminInterceptor } from './admin.interceptor';
import { AdminSessionService } from './admin-session.service';
import { AdminRole } from './admin-role';

describe('adminInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([adminInterceptor])),
        provideHttpClientTesting()
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function signIn(): void {
    TestBed.inject(AdminSessionService).start({
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      admin: { userId: 'u-1', userName: 'Oleg', roles: [AdminRole.AdminManagement] }
    });
  }

  it('should send the admin token to the API', () => {
    signIn();

    void firstValueFrom(http.get('/api/Admin/Me'));
    const req = httpMock.expectOne('/api/Admin/Me');

    expect(req.request.headers.get('Authorization')).toBe('Bearer admin-token');
    req.flush({});
  });

  it('should never send a workspace header', () => {
    signIn();

    void firstValueFrom(http.get('/api/Admin/Me'));
    const req = httpMock.expectOne('/api/Admin/Me');

    expect(req.request.headers.has('X-Workspace-Id')).toBeFalse();
    req.flush({});
  });

  it('should not send a token before sign-in', () => {
    void firstValueFrom(http.post('/api/Admin/Login', {}));
    const req = httpMock.expectOne('/api/Admin/Login');

    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });
});
