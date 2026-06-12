import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ProfileApiService } from './profile-api.service';
import { TokenService } from './token.service';
import { ProfileModel } from '../models/profile.model';
import { FormControl, FormGroup } from '@angular/forms';

describe('ProfileApiService', () => {
  let service: ProfileApiService;
  let httpMock: HttpTestingController;
  let tokenService: TokenService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        provideZonelessChangeDetection(),
        ProfileApiService,
        TokenService
      ]
    });

    service = TestBed.inject(ProfileApiService);
    httpMock = TestBed.inject(HttpTestingController);
    tokenService = TestBed.inject(TokenService);
    tokenService.Token = 'test_token';
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('save', () => {
    it('should save profile data without avatarId', async () => {
      const formGroup = new FormGroup({
        userName: new FormControl('TestUser'),
        comment: new FormControl('Test comment'),
        isAdult: new FormControl(true),
        hasAcceptedTerms: new FormControl(true)
      });

      const promise = service.save(formGroup);

      const req = httpMock.expectOne(request =>
        request.url.includes('Profile/Save') && request.method === 'POST'
      );
      expect(req.request.body).toEqual({
        userName: 'TestUser',
        comment: 'Test comment',
        isAdult: true,
        hasAcceptedTerms: true
      });
      req.flush({});

      await promise;
    });
  });

  describe('setAvatar', () => {
    it('should set avatar with valid avatarId', async () => {
      const avatarId = 5;

      const promise = service.setAvatar(avatarId);

      const req = httpMock.expectOne(request =>
        request.url.includes('Profile/SetAvatar') && request.method === 'POST'
      );
      expect(req.request.body).toEqual({ avatarId: 5 });
      req.flush({});

      await promise;
    });

    it('should handle error when setting avatar', async () => {
      const avatarId = 999;

      const promise = service.setAvatar(avatarId);

      const req = httpMock.expectOne(request =>
        request.url.includes('Profile/SetAvatar')
      );
      req.flush({ message: 'Invalid avatar ID' }, { status: 400, statusText: 'Bad Request' });

      await expectAsync(promise).toBeRejected();
    });
  });

  describe('details', () => {
    it('should fetch profile details', async () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 3,
        comment: 'Test comment',
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };

      const promise = service.details();

      const req = httpMock.expectOne(request =>
        request.url.includes('Profile/Details') && request.method === 'GET'
      );
      req.flush(mockProfile);

      const result = await promise;
      expect(result).toEqual(mockProfile);
    });
  });

  describe('view', () => {
    it('should fetch profile by userId', async () => {
      const userId = '456';
      const mockProfile: ProfileModel = {
        id: userId,
        userName: 'OtherUser',
        avatarId: 2,
        comment: null,
        isAdult: false,
        hasAcceptedTerms: true,
        walletAddress: null,
      };

      const promise = service.view(userId);

      const req = httpMock.expectOne(request =>
        request.url.includes(`Profile/View?userId=${userId}`) && request.method === 'GET'
      );
      req.flush(mockProfile);

      const result = await promise;
      expect(result).toEqual(mockProfile);
    });
  });
});
