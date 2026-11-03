import { TestBed } from '@angular/core/testing';
import { Injector, provideZonelessChangeDetection, runInInjectionContext } from '@angular/core';
import { ContextResource, contextResource, contextSignal } from './context-resource';
import { UserService } from './user.service';
import { profileDto, sessionDto } from '../api/dto.mock';

describe('contextResource', () => {
  let user: UserService;
  let calls: { workspaceId: string; signal: AbortSignal }[];

  function create(): ContextResource<string[]> {
    return runInInjectionContext(TestBed.inject(Injector), () => contextResource<string[]>({
      loader: async ({ workspaceId, abortSignal }) => {
        calls.push({ workspaceId, signal: abortSignal });
        return [`members of ${workspaceId}`];
      },
      defaultValue: []
    }));
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    calls = [];
    user = TestBed.inject(UserService);
  });

  afterEach(() => user.clearProfile());

  const signIn = () => user.applySession(sessionDto({
    token: 'token',
    profile: profileDto({ id: 'me', userName: 'Oleg', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
    permissionsByWorkspace: { 'ws-1': [], 'ws-2': [] }
  }), 'ws-1');

  // Без пространства читать нечего — и запроса нет
  it('should stay idle without an active workspace', async () => {
    const members = create();
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));

    expect(members.status()).toBe('idle');
    expect(members.value()).toEqual([]);
    expect(calls).toEqual([]);
  });

  it('should load for the active workspace and reset at once on switching', async () => {
    signIn();
    const members = create();
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
    expect(members.value()).toEqual(['members of ws-1']);

    user.switchWorkspace('ws-2');

    // До перезапуска загрузки — уже не ответ прошлого пространства
    expect(members.value()).toEqual([]);
    expect(members.status()).toBe('loading');

    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
    expect(members.value()).toEqual(['members of ws-2']);
  });

  // После команды экран перечитывает тот же контекст — прежний список стоит до ответа
  it('should keep the value while reloading the same context', async () => {
    signIn();
    const members = create();
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));

    members.reload();

    expect(members.value()).toEqual(['members of ws-1']);
    expect(members.status()).toBe('loading');
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
    expect(calls.length).toBe(2);
    expect(members.status()).toBe('resolved');
  });

  it('should reset a context signal when the workspace changes', () => {
    signIn();
    const editing = runInInjectionContext(TestBed.inject(Injector), () => contextSignal<string | null>(null));

    editing.set('u-1');
    expect(editing()).toBe('u-1');

    user.switchWorkspace('ws-2');
    expect(editing()).toBeNull();
  });
});
