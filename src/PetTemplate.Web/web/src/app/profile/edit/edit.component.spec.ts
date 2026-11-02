import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { EditComponent } from './edit.component';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { UserService } from '../../shared/core/user.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { ProfileModel } from '../../shared/models/profile.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountApiServiceMock } from '../../shared/api/account-api.service.mock';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';

describe('EditComponent', () => {
  let component: EditComponent;
  let fixture: ComponentFixture<EditComponent>;
  let profileApi: jasmine.SpyObj<ProfileApiService>;
  let userService: UserService;
  let router: Router;

  const profile: ProfileModel = {
    id: '123',
    userName: 'TestUser',
    avatarId: 5,
    comment: 'Test comment',
    isAdult: true,
    hasAcceptedTerms: true
  };

  beforeEach(async () => {
    profileApi = jasmine.createSpyObj<ProfileApiService>('ProfileApiService', ['details', 'save']);
    profileApi.details.and.resolveTo(profile);

    await TestBed.configureTestingModule({
      imports: [EditComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        provideRouter([]),
        { provide: ProfileApiService, useValue: profileApi },
        { provide: AccountApiService, useClass: AccountApiServiceMock }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    userService = TestBed.inject(UserService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);

    fixture = TestBed.createComponent(EditComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should load the profile into the form and the session', () => {
    expect(component.profileForm.value).toEqual({
      isAdult: true,
      hasAcceptedTerms: true
    });
    expect(userService.profile()).toEqual(profile);
  });

  it('should open with the main block first', async () => {
    await fixture.whenStable();

    const blocks = (fixture.nativeElement as HTMLElement).querySelectorAll('.card');
    expect(blocks[0].id).toBe('profile-logins');
  });

  /** Память ассистента обязана быть видимой (спец. assistant, 12.5) — вход в неё из профиля */
  it('should link to the assistant memory', async () => {
    await fixture.whenStable();

    const link = (fixture.nativeElement as HTMLElement).querySelector('#assistant-memory-button');
    expect(link?.getAttribute('href')).toBe('/profile/assistant-memory');
    expect(link?.textContent).toContain('Память ассистента');
  });

  /** Настройки пространств — и из профиля, не только шестерёнкой в шапке (спец. authorization, 7.2) */
  it('should link to the workspace settings', async () => {
    await fixture.whenStable();

    const link = (fixture.nativeElement as HTMLElement).querySelector('#workspace-settings-button');
    expect(link?.getAttribute('href')).toBe('/workspace/settings');
    expect(link?.textContent).toContain('Настройки пространств');
  });

  it('should show the chosen avatar', async () => {
    await fixture.whenStable();

    const img = (fixture.nativeElement as HTMLElement).querySelector('img');
    expect(img?.getAttribute('src')).toBe('/avatars/5.png');
  });

  it('should save a valid form and go home', async () => {
    profileApi.save.and.resolveTo({ ...profile, isAdult: true });

    await component.onSubmit();

    expect(profileApi.save).toHaveBeenCalledWith(component.profileForm);
    expect(userService.profile()?.userName).toBe('TestUser');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('should not save an invalid form and show why', async () => {
    component.profileForm.controls.isAdult.setValue(false);

    await component.onSubmit();
    await fixture.whenStable();

    expect(profileApi.save).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Поле «Мне есть 18 лет» обязательно');
  });

  it('should tell about a failed save', async () => {
    profileApi.save.and.rejectWith(new Error('Save failed'));

    await component.onSubmit();

    expect(TestBed.inject(AlertsService).alerts().map(a => a.content))
      .toEqual(['Не удалось сохранить профиль. Проверьте поля и попробуйте ещё раз']);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
