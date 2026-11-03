import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { UserService } from './user.service';
import { SessionResetService } from './session-reset.service';
import { TokenService } from '../api/token.service';
import { PermissionsService } from '../services/permissions.service';
import { PermissionEnum } from '../enums/permission.enum';
import { ProfileModel } from '../models/profile.model';
import { CURRENT_WORKSPACE_KEY, WorkspaceContextService } from './workspace-context.service';
import { SESSION_KEY, StoredSession } from './session-store';
import { profileDto, sessionDto } from '../api/dto.mock';

describe('UserService', () => {
  let service: UserService;
  let tokenService: TokenService;
  let permissions: PermissionsService;
  let sessionReset: SessionResetService;

  const profile: ProfileModel = profileDto({
    id: '123',
    userName: 'testuser',
    avatarId: null,
    comment: null,
    isAdult: false,
    hasAcceptedTerms: false
  });

  const other: ProfileModel = { ...profile, id: '456', userName: 'boris' };

  const signIn = (token = 'token', who: ProfileModel = profile) =>
    service.applySession(sessionDto({ token, profile: who, permissionsByWorkspace: {} }));

  /** Соседняя вкладка: пишет в хранилище сама, эта узнаёт событием `storage` */
  function otherTab(session: StoredSession | null) {
    write(session);
    window.dispatchEvent(new StorageEvent('storage', { key: SESSION_KEY }));
  }

  function write(session: StoredSession | null) {
    if (session) localStorage.setItem(SESSION_KEY, JSON.stringify(session));
    else localStorage.removeItem(SESSION_KEY);
  }

  const stored = () => JSON.parse(localStorage.getItem(SESSION_KEY) ?? 'null') as StoredSession | null;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });

    service = TestBed.inject(UserService);
    tokenService = TestBed.inject(TokenService);
    permissions = TestBed.inject(PermissionsService);
    sessionReset = TestBed.inject(SessionResetService);
  });


  it('should not be authenticated initially', () => {
    expect(service.profile()).toBeNull();
    expect(service.isAuthenticated).toBe(false);
  });

  it('should be authenticated when profile is set', () => {
    service.setProfile(profile);

    expect(service.profile()).toEqual(profile);
    expect(service.isAuthenticated).toBe(true);
  });

  // Токен и пользователь врозь разъезжались между вкладками — теперь одна запись
  it('should store the token and the user as one record', () => {
    signIn('token-1');

    expect(stored()).toEqual({ token: 'token-1', profile, permissionsByWorkspace: {} });
    expect(localStorage.getItem('token')).toBeNull();
    expect(localStorage.getItem('user')).toBeNull();
  });

  // Вход до выката с общей записью не выкидывает человека
  it('should move a session stored the old way into one record', () => {
    localStorage.setItem('token', 'legacy-token');
    localStorage.setItem('user', JSON.stringify({ profile, permissionsByWorkspace: {} }));

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const restored = TestBed.inject(UserService);

    expect(restored.profile()).toEqual(profile);
    expect(TestBed.inject(TokenService).Token).toBe('legacy-token');
    expect(localStorage.getItem('token')).toBeNull();
  });

  describe('logOut', () => {
    beforeEach(() => {
      signIn();
      permissions.initPermissions([PermissionEnum.PermissionManagement]);
    });

    it('should clear token, profile and permissions', async () => {
      await service.logOut();

      expect(tokenService.Token).toBe('');
      expect(service.isAuthenticated).toBe(false);
      expect(permissions.permissions()).toEqual([]);
      expect(stored()).toBeNull();
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
      signIn();
      let seen: { canPersist: boolean; authenticated: boolean } | undefined;
      sessionReset.register(options => {
        seen = { canPersist: options.canPersist, authenticated: service.isAuthenticated };
      });

      await service.expireSession('token');

      expect(seen).toEqual({ canPersist: false, authenticated: false });
      expect(tokenService.Token).toBe('');
    });

    // 401 на старый токен этой вкладки не стирает свежий, записанный соседней
    it('should keep a fresh session another tab wrote meanwhile', async () => {
      signIn('stale');
      localStorage.setItem(SESSION_KEY, JSON.stringify(sessionDto({ token: 'fresh', profile: other, permissionsByWorkspace: {} })));
      const reset = vi.fn();
      sessionReset.register(reset);

      await service.expireSession('stale');

      expect(stored()?.token).toBe('fresh');
      expect(service.isAuthenticated).toBe(true);
      expect(service.profile()?.id).toBe('456');
      expect(tokenService.Token).toBe('fresh');
      // Человек в вкладке сменился — разделы прежнего убираются, без сервера
      expect(reset).toHaveBeenCalledWith({ canPersist: false });
    });
  });

  describe('other tabs', () => {
    // Своего кеша у токена нет: запрос уходит с тем, что лежит в хранилище сейчас
    it('should send the token another tab has just renewed', () => {
      signIn('old');
      localStorage.setItem(SESSION_KEY, JSON.stringify(sessionDto({ token: 'renewed', profile, permissionsByWorkspace: {} })));

      expect(tokenService.Token).toBe('renewed');
    });

    it('should follow another tab signing in as someone else without writing back', async () => {
      signIn('token-a');
      const reset = vi.fn();
      sessionReset.register(reset);
      write(sessionDto({ token: 'token-b', profile: other, permissionsByWorkspace: { 'ws-b': [PermissionEnum.WorkspaceMemberRead] } }));
      const writes = vi.spyOn(Storage.prototype, 'setItem');

      window.dispatchEvent(new StorageEvent('storage', { key: SESSION_KEY }));

      expect(service.profile()?.id).toBe('456');
      expect(service.workspaceIds()).toEqual(['ws-b']);
      expect(permissions.permissions()).toEqual([PermissionEnum.WorkspaceMemberRead]);
      expect(reset).toHaveBeenCalledWith({ canPersist: false });
      expect(writes.mock.calls.some(([key]) => key === SESSION_KEY)).toBe(false);
    });

    it('should end the session when another tab logs out', () => {
      signIn();

      otherTab(null);

      expect(service.isAuthenticated).toBe(false);
      expect(tokenService.Token).toBe('');
    });

    // Ответ сервера про прежнего человека не ложится рядом с токеном нового
    it('should not put a profile next to another user session', () => {
      signIn('token-a');
      localStorage.setItem(SESSION_KEY, JSON.stringify(sessionDto({ token: 'token-b', profile: other, permissionsByWorkspace: {} })));

      service.setProfile({ ...profile, userName: 'renamed' });

      expect(stored()?.profile.id).toBe('456');
      expect(stored()?.token).toBe('token-b');
      expect(service.profile()?.id).toBe('456');
    });

    it('should keep the stored token when the profile of the same user is updated', () => {
      signIn('old');
      localStorage.setItem(SESSION_KEY, JSON.stringify(sessionDto({ token: 'renewed', profile, permissionsByWorkspace: {} })));

      service.setProfile({ ...profile, userName: 'renamed' });

      expect(stored()).toEqual({ token: 'renewed', profile: { ...profile, userName: 'renamed' }, permissionsByWorkspace: {} });
    });
  });

  describe('applySession', () => {
    const session = (primaryWorkspaceId: string | null) => (sessionDto({
      token: 'token',
      profile: { ...profile, primaryWorkspaceId },
      permissionsByWorkspace: {
        'ws-1': [PermissionEnum.WorkspaceMemberRead],
        'ws-2': [PermissionEnum.PermissionManagement]
      }
    }));

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

    // Перевыпуск сессии того же человека оставляет пространство вкладки
    it('should keep the tab workspace when the session is renewed', () => {
      service.applySession(session('ws-1'), 'ws-2');

      service.renewSession(session('ws-1'));

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-2');
    });

    // Принятое приглашение — единственное новое пространство — становится активным (6.3)
    it('should activate the workspace of an accepted invitation', () => {
      service.applySession({ ...session('ws-1'), permissionsByWorkspace: { 'ws-1': [] } });

      service.applyAcceptedInvitation(session('ws-1'));

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-2');
    });

    it('should clear the active workspace when there are none', () => {
      TestBed.inject(WorkspaceContextService).setCurrentWorkspaceId('ws-old');

      service.applySession(sessionDto({ token: 'token', profile, permissionsByWorkspace: {} }));

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBeNull();
      expect(permissions.permissions()).toEqual([]);
    });

    // Сессия переживает перезагрузку страницы: новый сервис поднимает её из хранилища
    it('should restore the session and the active workspace permissions after reload', () => {
      service.applySession(session('ws-2'));

      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
      const restored = TestBed.inject(UserService);

      expect(restored.isAuthenticated).toBe(true);
      expect(restored.workspaceIds()).toEqual(['ws-1', 'ws-2']);
      expect(TestBed.inject(PermissionsService).permissions()).toEqual([PermissionEnum.PermissionManagement]);
    });

    // Активное пространство — своё у вкладки: перезагрузка берёт его из `sessionStorage`,
    // а не последний выбор соседней вкладки из `localStorage`
    it('should restore the workspace of this tab rather than the shared default', () => {
      service.applySession(session('ws-1'), 'ws-2');
      localStorage.setItem(CURRENT_WORKSPACE_KEY, 'ws-1');

      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
      TestBed.inject(UserService);

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-2');
    });

    it('should forget the active workspace on log out', async () => {
      service.applySession(session('ws-1'));

      await service.logOut();

      expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBeNull();
      expect(service.workspaceIds()).toEqual([]);
    });
  });
});
