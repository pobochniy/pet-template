import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { UserService } from './user.service';
import { SessionResetService } from './session-reset.service';
import { TokenService } from '../api/token.service';
import { PermissionsService } from '../services/permissions.service';
import { PermissionEnum } from '../enums/permission.enum';
import { ProfileModel } from '../models/profile.model';
import { WorkspaceContextService } from './workspace-context.service';

describe('UserService', () => {
  let service: UserService;
  let tokenService: TokenService;
  let permissions: PermissionsService;
  let sessionReset: SessionResetService;

  const profile: ProfileModel = {
    id: '123',
    userName: 'testuser',
    avatarId: null,
    comment: null,
    isAdult: false,
    hasAcceptedTerms: false
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });

    service = TestBed.inject(UserService);
    tokenService = TestBed.inject(TokenService);
    permissions = TestBed.inject(PermissionsService);
    sessionReset = TestBed.inject(SessionResetService);
  });


  it('should not be authenticated initially', () => {
    expect(service.profile()).toBeNull();
    expect(service.isAuthenticated).toBeFalse();
  });

  it('should be authenticated when profile is set', () => {
    service.setProfile(profile);

    expect(service.profile()).toEqual(profile);
    expect(service.isAuthenticated).toBeTrue();
  });

  describe('logOut', () => {
    beforeEach(() => {
      tokenService.Token = 'token';
      service.setProfile(profile);
      permissions.initPermissions([PermissionEnum.PermissionManagement]);
    });

    it('should clear token, profile and permissions', async () => {
      await service.logOut();

      expect(tokenService.Token).toBe('');
      expect(service.isAuthenticated).toBeFalse();
      expect(permissions.permissions()).toEqual([]);
    });

    // Уборке разделов может понадобиться сервер — токен к этому моменту ещё на месте
    it('should run the section cleanup while the session is still alive', async () => {
      let tokenDuringCleanup = '';
      sessionReset.register(() => {
        tokenDuringCleanup = tokenService.Token;
      });

      await service.logOut();

      expect(tokenDuringCleanup).toBe('token');
    });
  });

  describe('expireSession', () => {
    // Сервер токен уже не принял: сессия обнуляется сразу, а разделы узнают, что сохранять нечем
    it('should clear the session first and tell handlers they cannot persist', async () => {
      tokenService.Token = 'token';
      service.setProfile(profile);
      let seen: { canPersist: boolean; authenticated: boolean } | undefined;
      sessionReset.register(options => {
        seen = { canPersist: options.canPersist, authenticated: service.isAuthenticated };
      });

      await service.expireSession();

      expect(seen).toEqual({ canPersist: false, authenticated: false });
      expect(tokenService.Token).toBe('');
    });
  });

  describe('applySession', () => {
    const session = (primaryWorkspaceId: string | null) => ({
      token: 'token',
      profile: { ...profile, primaryWorkspaceId },
      permissionsByWorkspace: {
        'ws-1': [PermissionEnum.WorkspaceMemberRead],
        'ws-2': [PermissionEnum.PermissionManagement]
      }
    });

    // После входа активным становится основное, и права — его (спец. authorization, 3)
    it('should activate the primary workspace with its permissions', () => {
      service.applySession(session('ws-2'));

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-2');
      expect(permissions.permissions()).toEqual([PermissionEnum.PermissionManagement]);
    });

    // Указатель на пространство, которого в сессии нет, — первое доступное, а не чужое
    it('should fall back to an own workspace when the primary is unavailable', () => {
      service.applySession(session('ws-foreign'));

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-1');
    });

    // Созданное или принятое пространство становится активным, а не основное
    it('should activate the requested workspace', () => {
      service.applySession(session('ws-1'), 'ws-2');

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-2');
    });

    it('should clear the active workspace when there are none', () => {
      TestBed.inject(WorkspaceContextService).setCurrentWorkspaceId('ws-old');

      service.applySession({ token: 'token', profile, permissionsByWorkspace: {} });

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBeNull();
      expect(permissions.permissions()).toEqual([]);
    });

    // Сессия переживает перезагрузку страницы: новый сервис поднимает её из хранилища
    it('should restore the session and the active workspace permissions after reload', () => {
      service.applySession(session('ws-2'));

      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
      const restored = TestBed.inject(UserService);

      expect(restored.isAuthenticated).toBeTrue();
      expect(restored.workspaceIds()).toEqual(['ws-1', 'ws-2']);
      expect(TestBed.inject(PermissionsService).permissions()).toEqual([PermissionEnum.PermissionManagement]);
    });

    it('should forget the active workspace on log out', async () => {
      service.applySession(session('ws-1'));

      await service.logOut();

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBeNull();
      expect(service.workspaceIds()).toEqual([]);
    });
  });
});
