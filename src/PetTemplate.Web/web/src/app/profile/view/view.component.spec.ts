import type { Mocked } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ViewComponent } from './view.component';
import { ProfileApiService } from '../profile-api.service';
import { ProfileModel } from '../../shared/models/profile.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { profileDto } from '../../shared/api/dto.mock';

describe('ViewComponent', () => {
  let profileApi: Mocked<ProfileApiService>;
  let router: Mocked<Router>;

  const profile: ProfileModel = profileDto({
    id: '456',
    userName: 'OtherUser',
    avatarId: 2,
    comment: null,
    isAdult: true,
    hasAcceptedTerms: false
  });

  async function create(userId: string | null) {
    profileApi = { view: vi.fn() } as unknown as Mocked<ProfileApiService>;
    router = { navigateByUrl: vi.fn() } as unknown as Mocked<Router>;
    router.navigateByUrl.mockResolvedValue(true);

    await TestBed.configureTestingModule({
      imports: [ViewComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ProfileApiService, useValue: profileApi },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => userId } } } }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));

    return TestBed.createComponent(ViewComponent);
  }

  it('should show the profile by userId', async () => {
    const fixture = await create('456');
    profileApi.view.mockResolvedValue(profile);

    await fixture.whenStable();
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(profileApi.view).toHaveBeenCalledWith('456');
    expect(text).toContain('OtherUser');
    expect(text).toContain('Не указан');
  });

  it('should go home without userId', async () => {
    const fixture = await create(null);

    await fixture.componentInstance.ngOnInit();

    expect(profileApi.view).not.toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('should go home when the profile is not found', async () => {
    const fixture = await create('456');
    profileApi.view.mockResolvedValue(undefined as never);

    await fixture.componentInstance.ngOnInit();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });
});
