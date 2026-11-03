import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { FormControl, FormGroup } from '@angular/forms';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { provideZonelessChangeDetection } from '@angular/core';
import { AuthApiService } from './auth-api.service';
import { TokenService } from './token.service';
import { UserService } from '../core/user.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { profileDto, sessionDto } from './dto.mock';

describe('AuthApiService', () => {
  let service: AuthApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(AuthApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // Api-сервис только спрашивает сервер: сессию раскладывает экран через `UserService`
  it('should return the login response without touching the session', async () => {
    const form = new FormGroup({ login: new FormControl('tester'), password: new FormControl('secret-1') });
    const response: AuthenticationResultModel = sessionDto({
      token: 'fresh',
      profile: profileDto({ id: 'u-1', userName: 'tester', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [] }
    });

    const promise = service.login(form);
    const req = httpMock.expectOne('/api/Auth/Login');
    expect(req.request.withCredentials).toBe(false);
    req.flush(response);

    expect(await promise).toEqual(response);
    expect(TestBed.inject(UserService).isAuthenticated).toBe(false);
    expect(TestBed.inject(TokenService).Token).toBe('');
  });
});
