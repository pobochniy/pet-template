import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Avatar } from './avatar';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { UserService } from '../../shared/core/user.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { ProfileModel } from '../../shared/models/profile.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('Avatar', () => {
  let component: Avatar;
  let fixture: ComponentFixture<Avatar>;
  let profileApi: jasmine.SpyObj<ProfileApiService>;
  let userService: UserService;

  const profile: ProfileModel = {
    id: '123',
    userName: 'TestUser',
    avatarId: 3,
    comment: null,
    isAdult: true,
    hasAcceptedTerms: true
  };

  beforeEach(async () => {
    profileApi = jasmine.createSpyObj<ProfileApiService>('ProfileApiService', ['details', 'setAvatar']);

    await TestBed.configureTestingModule({
      imports: [Avatar],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: ProfileApiService, useValue: profileApi }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    userService = TestBed.inject(UserService);
    fixture = TestBed.createComponent(Avatar);
    component = fixture.componentInstance;
  });

  it('should not load the profile when it is already in the session', async () => {
    userService.setProfile(profile);

    await component.ngOnInit();

    expect(profileApi.details).not.toHaveBeenCalled();
  });

  it('should load the profile when the session has none', async () => {
    profileApi.details.and.resolveTo(profile);

    await component.ngOnInit();

    expect(userService.profile()).toEqual(profile);
    expect(component.currentAvatarId()).toBe(3);
  });

  it('should fall back to avatar 0 without a profile', () => {
    expect(component.currentAvatarId()).toBe(0);
  });

  it('should save the picked avatar and mark it as selected', async () => {
    userService.setProfile(profile);
    profileApi.setAvatar.and.resolveTo({});
    await fixture.whenStable();

    const options = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.avatar-option');
    expect(options.length).toBe(8);
    options[5].click();
    await fixture.whenStable();

    expect(profileApi.setAvatar).toHaveBeenCalledWith(5);
    expect(userService.profile()?.avatarId).toBe(5);
    expect(options[5].getAttribute('aria-pressed')).toBe('true');
    expect(options[5].textContent).toContain('Выбран');
  });

  it('should tell about a failed change', async () => {
    userService.setProfile(profile);
    profileApi.setAvatar.and.rejectWith(new Error('Invalid avatar'));

    await component.selectAvatar(5);

    expect(userService.profile()?.avatarId).toBe(3);
    expect(TestBed.inject(AlertsService).alerts().map(a => a.content)).toEqual(['Не удалось сменить аватар']);
  });
});
