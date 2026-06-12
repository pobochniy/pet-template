import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, ActivatedRoute } from '@angular/router';
import { provideZonelessChangeDetection } from '@angular/core';
import { EditComponent } from './edit.component';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { UserService } from '../../shared/services/user.service';
import { ProfileModel } from '../../shared/models/profile.model';

describe('EditComponent', () => {
  let component: EditComponent;
  let fixture: ComponentFixture<EditComponent>;
  let mockProfileService: jasmine.SpyObj<ProfileApiService>;
  let mockRouter: jasmine.SpyObj<Router>;
  let mockUserService: jasmine.SpyObj<UserService>;

  beforeEach(async () => {
    mockProfileService = jasmine.createSpyObj('ProfileApiService', ['details', 'save']);
    mockRouter = jasmine.createSpyObj('Router', ['navigateByUrl']);
    mockUserService = jasmine.createSpyObj('UserService', ['profile', 'setProfile']);

    await TestBed.configureTestingModule({
      imports: [EditComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ProfileApiService, useValue: mockProfileService },
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

    fixture = TestBed.createComponent(EditComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    // Сбрасываем форму после каждого теста к начальным значениям
    component.profileForm.reset({
      userName: '',
      comment: '',
      isAdult: false,
      hasAcceptedTerms: false
    });
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('ngOnInit', () => {
    it('should load profile and set form values', async () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 5,
        comment: 'Test comment',
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };

      mockProfileService.details.and.returnValue(Promise.resolve(mockProfile));

      await component.ngOnInit();

      expect(mockProfileService.details).toHaveBeenCalled();
      expect(mockUserService.setProfile).toHaveBeenCalledWith(mockProfile);
      expect(component.profileForm.value).toEqual({
        userName: 'TestUser',
        comment: 'Test comment',
        isAdult: true,
        hasAcceptedTerms: true
      });
    });

    it('should not set form if profile is null', async () => {
      mockProfileService.details.and.returnValue(Promise.resolve(null as any));

      await component.ngOnInit();

      // Форма должна остаться с начальными значениями (пустая строка)
      expect(component.profileForm.value.userName).toBe('');
      expect(mockUserService.setProfile).not.toHaveBeenCalled();
    });
  });

  describe('getAvatarUrl', () => {
    it('should return correct avatar URL from userService profile', () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 3,
        comment: null,
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };
      mockUserService.profile.and.returnValue(mockProfile);
      expect(component.getAvatarUrl()).toBe('/avatars/3.png');
    });

    it('should return default avatar URL when avatarId is null', () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: null,
        comment: null,
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };
      mockUserService.profile.and.returnValue(mockProfile);
      expect(component.getAvatarUrl()).toBe('/avatars/0.png');
    });

    it('should return default avatar URL when profile is null', () => {
      mockUserService.profile.and.returnValue(null);
      expect(component.getAvatarUrl()).toBe('/avatars/0.png');
    });
  });

  describe('onSubmit', () => {
    beforeEach(async () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 2,
        comment: 'Test',
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null,
      };
      mockProfileService.details.and.returnValue(Promise.resolve(mockProfile));
      await component.ngOnInit();
    });

    it('should save valid form and navigate to home', async () => {
      component.profileForm.patchValue({
        userName: 'UpdatedUser',
        comment: 'Updated comment'
      });

      mockProfileService.save.and.returnValue(Promise.resolve());
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      await component.onSubmit();

      expect(mockProfileService.save).toHaveBeenCalledWith(component.profileForm);
      expect(mockRouter.navigateByUrl).toHaveBeenCalledWith('/');
    });

    it('should not save invalid form', async () => {
      component.profileForm.patchValue({
        userName: '',
        isAdult: false,
        hasAcceptedTerms: false
      });

      await component.onSubmit();

      expect(mockProfileService.save).not.toHaveBeenCalled();
      expect(mockRouter.navigateByUrl).not.toHaveBeenCalled();
    });

    it('should handle save error', async () => {
      spyOn(window, 'alert');
      component.profileForm.patchValue({
        userName: 'TestUser'
      });

      mockProfileService.save.and.returnValue(Promise.reject(new Error('Save failed')));

      await component.onSubmit();

      expect(window.alert).toHaveBeenCalledWith('Возникли непредвиденные ошибки. Попробуйте ввести другие значения или сообщите программисту');
    });

    it('should mark all fields as dirty on submit', async () => {
      component.profileForm.patchValue({
        userName: 'TestUser',
        comment: 'Test',
        isAdult: true,
        hasAcceptedTerms: true
      });

      mockProfileService.save.and.returnValue(Promise.resolve());
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      await component.onSubmit();

      Object.keys(component.profileForm.controls).forEach(key => {
        expect(component.profileForm.get(key)?.dirty).toBe(true);
      });
    });
  });

  describe('maxLength', () => {
    it('should limit value to 9999', () => {
      const event = {
        target: { value: 10000 }
      };

      component.maxLength(event);

      expect(event.target.value).toBe(9999);
    });

    it('should not change value if it is less than 9999', () => {
      const event = {
        target: { value: 5000 }
      };

      component.maxLength(event);

      expect(event.target.value).toBe(5000);
    });
  });
});
