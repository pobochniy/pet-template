import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, ActivatedRoute } from '@angular/router';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { Avatar } from './avatar';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { UserService } from '../../shared/services/user.service';
import { ProfileModel } from '../../shared/models/profile.model';

describe('Avatar', () => {
  let component: Avatar;
  let fixture: ComponentFixture<Avatar>;
  let mockProfileApi: jasmine.SpyObj<ProfileApiService>;
  let mockRouter: jasmine.SpyObj<Router>;
  let mockUserService: jasmine.SpyObj<UserService>;

  let profileSignal: ReturnType<typeof signal<ProfileModel | null>>;

  beforeEach(async () => {
    mockProfileApi = jasmine.createSpyObj('ProfileApiService', ['setAvatar', 'details']);
    mockRouter = jasmine.createSpyObj('Router', ['navigateByUrl']);

    profileSignal = signal<ProfileModel | null>(null);
    mockUserService = jasmine.createSpyObj('UserService', ['setProfile']);
    Object.defineProperty(mockUserService, 'profile', {
      get: () => profileSignal,
      configurable: true
    });

    await TestBed.configureTestingModule({
      imports: [Avatar],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ProfileApiService, useValue: mockProfileApi },
        { provide: Router, useValue: mockRouter },
        { provide: UserService, useValue: mockUserService },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => null } }
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(Avatar);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('ngOnInit', () => {
    it('should not load profile if already exists', async () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 3,
        comment: null,
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };

      profileSignal.set(mockProfile);

      await component.ngOnInit();

      expect(mockProfileApi.details).not.toHaveBeenCalled();
    });

    it('should load profile if not exists', async () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 5,
        comment: null,
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };

      profileSignal.set(null);
      mockProfileApi.details.and.returnValue(Promise.resolve(mockProfile));

      await component.ngOnInit();

      expect(mockProfileApi.details).toHaveBeenCalled();
      expect(mockUserService.setProfile).toHaveBeenCalledWith(mockProfile);
    });

    it('should not set profile if API returns null', async () => {
      profileSignal.set(null);
      mockProfileApi.details.and.returnValue(Promise.resolve(null as any));

      await component.ngOnInit();

      expect(mockProfileApi.details).toHaveBeenCalled();
      expect(mockUserService.setProfile).not.toHaveBeenCalled();
    });
  });

  describe('selectAvatar', () => {
    beforeEach(() => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 2,
        comment: null,
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };
      profileSignal.set(mockProfile);
    });

    it('should set avatar and update user service', async () => {
      const newAvatarId = 5;
      mockProfileApi.setAvatar.and.returnValue(Promise.resolve());

      await component.selectAvatar(newAvatarId);

      expect(mockProfileApi.setAvatar).toHaveBeenCalledWith(newAvatarId);
      expect(mockUserService.setProfile).toHaveBeenCalledWith(jasmine.objectContaining({
        avatarId: newAvatarId
      }));
    });

    it('should handle error when setting avatar', async () => {
      spyOn(window, 'alert');
      spyOn(console, 'error');
      const newAvatarId = 7;
      mockProfileApi.setAvatar.and.returnValue(Promise.reject(new Error('API Error')));

      await component.selectAvatar(newAvatarId);

      expect(window.alert).toHaveBeenCalledWith('Ошибка при установке аватара');
      expect(console.error).toHaveBeenCalledWith('Error setting avatar:', jasmine.any(Error));
    });

    it('should not update if profile is null', async () => {
      profileSignal.set(null);
      mockProfileApi.setAvatar.and.returnValue(Promise.resolve());

      await component.selectAvatar(3);

      expect(mockUserService.setProfile).not.toHaveBeenCalled();
    });
  });

  describe('getAvatarUrl', () => {
    it('should return correct avatar URL', () => {
      expect(component.getAvatarUrl(0)).toBe('/avatars/0.png');
      expect(component.getAvatarUrl(3)).toBe('/avatars/3.png');
      expect(component.getAvatarUrl(7)).toBe('/avatars/7.png');
    });
  });

  describe('availableAvatars', () => {
    it('should have avatars from 0 to 7', () => {
      expect(component.availableAvatars).toEqual([0, 1, 2, 3, 4, 5, 6, 7]);
    });
  });

  describe('currentAvatarId computed', () => {
    it('should return avatarId from profile', () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 4,
        comment: null,
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };
      profileSignal.set(mockProfile);

      expect(component.currentAvatarId()).toBe(4);
    });

    it('should return 0 when avatarId is null', () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: null,
        comment: null,
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };
      profileSignal.set(mockProfile);

      expect(component.currentAvatarId()).toBe(0);
    });

    it('should return 0 when profile is null', () => {
      profileSignal.set(null);

      expect(component.currentAvatarId()).toBe(0);
    });
  });
});
