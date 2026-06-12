import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { UserService } from './user.service';
import { ProfileModel } from '../models/profile.model';

describe('UserService', () => {
  let service: UserService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()]
    });
    service = TestBed.inject(UserService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should initialize with null profile', () => {
    expect(service.profile()).toBeNull();
  });

  it('should not be authenticated initially', () => {
    expect(service.isAuthenticated).toBe(false);
  });

  it('should set profile', () => {
    const profile: ProfileModel = {
      id: '123',
      userName: 'testuser',
      avatarId: null,
      comment: null,
      isAdult: false,
      hasAcceptedTerms: false,
      walletAddress: null
    };
    service.setProfile(profile);
    expect(service.profile()).toEqual(profile);
  });

  it('should be authenticated when profile is set', () => {
    const profile: ProfileModel = {
      id: '123',
      userName: 'testuser',
      avatarId: null,
      comment: null,
      isAdult: false,
      hasAcceptedTerms: false,
      walletAddress: null
    };
    service.setProfile(profile);
    expect(service.isAuthenticated).toBe(true);
  });

  it('should clear profile', () => {
    const profile: ProfileModel = {
      id: '123',
      userName: 'testuser',
      avatarId: null,
      comment: null,
      isAdult: false,
      hasAcceptedTerms: false,
      walletAddress: null
    };
    service.setProfile(profile);
    service.clearProfile();
    expect(service.profile()).toBeNull();
  });

  it('should not be authenticated after clearing profile', () => {
    const profile: ProfileModel = {
      id: '123',
      userName: 'testuser',
      avatarId: null,
      comment: null,
      isAdult: false,
      hasAcceptedTerms: false,
      walletAddress: null
    };
    service.setProfile(profile);
    service.clearProfile();
    expect(service.isAuthenticated).toBe(false);
  });
});
