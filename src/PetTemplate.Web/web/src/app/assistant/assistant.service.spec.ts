import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { AssistantService } from './assistant.service';
import { AssistantApiService } from '../shared/api/assistant-api.service';
import { AssistantApiServiceMock } from '../shared/api/assistant-api.service.mock';
import { UserService } from '../shared/core/user.service';
import { AssistantHistory, AssistantMessageRole, AssistantRun, AssistantRunStatus } from '../shared/models/assistant.model';
import { assistantHistoryDto, assistantMessageDto, assistantRunDto, profileDto, sessionDto } from '../shared/api/dto.mock';

/**
 * Ответы про прежний тред (12.1) не ложатся в ленту: смена пространства и закрытие
 * экрана обрывают запросы, а не только перестают их ждать
 */
describe('AssistantService', () => {
  let service: AssistantService;
  let api: AssistantApiServiceMock;
  let user: UserService;

  const message = (id: string, content: string) =>
    (assistantMessageDto({ id, role: AssistantMessageRole.User, content, createdAtUtc: new Date().toISOString() }));

  /** Ответ, который приходит, когда скажет тест; `signal` — тот, что ушёл в запрос */
  function deferred<T>() {
    let resolve!: (value: T) => void;
    let signal: AbortSignal | undefined;
    const call = (s?: AbortSignal) => {
      signal = s;
      return new Promise<T>((res, reject) => {
        resolve = res;
        s?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
      });
    };

    return { call, resolve: (value: T) => resolve(value), signal: () => signal };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        AssistantApiServiceMock,
        { provide: AssistantApiService, useExisting: AssistantApiServiceMock }
      ]
    });

    api = TestBed.inject(AssistantApiServiceMock);
    user = TestBed.inject(UserService);
    user.applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: 'me', userName: 'Vasya', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [], 'ws-2': [] }
    }), 'ws-1');
    service = TestBed.inject(AssistantService);
  });

  afterEach(() => user.clearProfile());

  it('should drop the feed of the previous workspace that arrives after switching', async () => {
    const old = deferred<AssistantHistory>();
    let calls = 0;
    vi.spyOn(api, 'History').mockImplementation((_cursor?: string, signal?: AbortSignal) =>
      calls++ === 0 ? old.call(signal) : Promise.resolve(assistantHistoryDto({ messages: [message('m2', 'вопрос на работе')] })));

    const opening = service.open();
    user.switchWorkspace('ws-2');
    TestBed.tick();

    expect(old.signal()?.aborted).toBe(true);

    old.resolve(assistantHistoryDto({ messages: [message('m1', 'вопрос в семье')] }));
    await opening;
    await new Promise(resolve => setTimeout(resolve));

    expect(service.messages().map(x => x.content)).toEqual(['вопрос на работе']);
  });

  it('should abort the long poll when the chat closes', async () => {
    api.setHistory(assistantHistoryDto({ messages: [] }));
    await service.open();

    const poll = deferred<AssistantRun>();
    vi.spyOn(api, 'Run').mockImplementation((_id: string, _wait?: number, signal?: AbortSignal) => poll.call(signal));

    const watching = service.watch('run-1');
    service.close();
    await watching;

    expect(poll.signal()?.aborted).toBe(true);
    expect(service.activeRun()).toBeUndefined();
  });

  it('should not start polling for a closed chat', async () => {
    const run = vi.spyOn(api, 'Run').mockResolvedValue(assistantRunDto({
      id: 'run-1', status: AssistantRunStatus.Running, step: 0, stepsHard: 8, escalated: false
    }));

    await service.watch('run-1');

    expect(run).not.toHaveBeenCalled();
  });

  /**
   * Реплика, отправленная во время остановки хода (3), ждёт в очереди треда без прогона:
   * следующий ход заводит старый, когда допишет своё. Клиент перечитывает ленту, пока
   * этот ход не появится, — иначе узнал бы о нём только после перезагрузки
   */
  it('should pick up the run that starts after the stopping one', async () => {
    const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
    const run = (id: string, status: AssistantRunStatus): AssistantRun =>
      (assistantRunDto({ id, status, step: 0, stepsHard: 8, escalated: false }));

    api.setHistory(assistantHistoryDto({ messages: [] }));
    await service.open();

    vi.useFakeTimers();
    try {
      api.setRun(run('run-1', AssistantRunStatus.Stopped));
      api.setHistory(assistantHistoryDto({ messages: [{ ...message('m2', 'и ещё вопрос'), runId: null }], activeRun: null }));

      void service.watch('run-1');
      await flush();
      expect(service.running()).toBe(false);

      api.setRun(run('run-2', AssistantRunStatus.Running));
      api.setHistory(assistantHistoryDto({
        messages: [{ ...message('m2', 'и ещё вопрос'), runId: 'run-2' }],
        activeRun: run('run-2', AssistantRunStatus.Running)
      }));
      vi.advanceTimersByTime(3000);
      await flush();

      expect(service.activeRun()?.id).toBe('run-2');
    } finally {
      service.close();
      vi.useRealTimers();
    }
  });
});
