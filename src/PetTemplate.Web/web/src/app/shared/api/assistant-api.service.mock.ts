import { Injectable } from '@angular/core';
import { IAssistantApiService } from './assistant-api.service';
import { serverRefusal } from './server-refusal.mock';
import {
  AssistantHistory, AssistantMemory, AssistantMessage, AssistantMessageRole, AssistantRun, AssistantRunStatus,
  AssistantSendResult, AssistantTrace
} from '../models/assistant.model';

/** Отказы `Send` кодом, которые умеет сервер (спец. assistant, 13.6 и 14) */
export type AssistantRefusal = 'dailyLimitReached' | 'monthlyLimitReached' | 'modelUnavailable';

/** Отказы `Transcribe` кодом (спец. assistant, 6) — лимит пространства тот же, что у `Send` */
export type TranscribeRefusal =
  'transcriptionFailed' | 'transcriptionUnavailable' | 'audioTooLarge' | 'audioTooLong'
  | 'dailyLimitReached' | 'monthlyLimitReached';

/** Потолок записи на сервере — минута (6.3) */
const MAX_SECONDS = 60;

/**
 * Ассистент в тестах компонентов: ответы задаются заранее, сети нет.
 *
 * Ходы здесь не идут: `Send` кладёт реплику в ленту и отдаёт прогон, а что
 * ответит ассистент, решает тест — так же, как в жизни решает модель.
 */
@Injectable()
export class AssistantApiServiceMock implements IAssistantApiService {
  private history: AssistantHistory = { messages: [] };
  private runs = new Map<string, AssistantRun>();
  private trace: AssistantTrace = { expired: false, steps: [] };
  private failure: string | null = null;
  private refusal: { code: AssistantRefusal; workspace: string } | null = null;
  private memory: AssistantMemory = { shared: [], personal: [] };
  private transcribeRefusal: { code: TranscribeRefusal; workspace: string } | null = null;

  /** Что расшифрует «движок» */
  public transcription = '';

  /** Длительности записей, ушедших на расшифровку */
  public readonly transcribed: number[] = [];

  public readonly forgotten: string[] = [];

  /** Что уехало на сервер — по нему видно, что отправил экран */
  public readonly sent: string[] = [];

  public readonly stopped: string[] = [];

  /** Курсоры, с которыми просили ленту: `undefined` — последняя страница */
  public readonly historyCursors: (string | undefined)[] = [];

  public cleared = 0;

  public setHistory(history: AssistantHistory) {
    this.history = history;
  }

  public setRun(run: AssistantRun) {
    this.runs.set(run.id, run);
  }

  public setTrace(trace: AssistantTrace) {
    this.trace = trace;
  }

  /**
   * `Send` отказывает кодом, как сервер: лимит пространства (13.6) — с его названием
   * подстановкой, «ассистент недоступен» (16.2) — без. Отказ держится, пока его
   * не снимут: сервер тоже отказывает на каждый `Send`
   */
  public refuseSend(code: AssistantRefusal | null, workspace = '') {
    this.refusal = code == null ? null : { code, workspace };
  }

  public setMemory(memory: AssistantMemory) {
    this.memory = memory;
  }

  /** `Transcribe` отказывает кодом, пока отказ не снимут */
  public refuseTranscribe(code: TranscribeRefusal | null, workspace = '') {
    this.transcribeRefusal = code == null ? null : { code, workspace };
  }

  /** Следующий вызов отвечает отказом без кода — так проверяется «прочая» ошибка */
  public failWith(message: string) {
    this.failure = message;
  }

  public async History(cursor?: string): Promise<AssistantHistory> {
    this.historyCursors.push(cursor);
    this.failIfConfigured();

    return this.history;
  }

  public async Send(text: string): Promise<AssistantSendResult> {
    this.failIfConfigured();

    if (this.refusal != null) {
      const { code, workspace } = this.refusal;
      throw serverRefusal(400, `Mock: ${code}`, `assistant.error.${code}`,
        code === 'modelUnavailable' ? undefined : { workspace });
    }
    this.sent.push(text);

    const runId = `run-${this.sent.length}`;
    const message: AssistantMessage = {
      id: `message-${this.history.messages.length + 1}`,
      role: AssistantMessageRole.User,
      content: text,
      runId,
      createdAtUtc: new Date().toISOString()
    };
    this.history = { ...this.history, messages: [...this.history.messages, message] };

    // Ход, заданный тестом заранее, не перетираем: так тест говорит, чем кончится
    // этот ход, — в жизни это решает модель
    if (!this.runs.has(runId))
      this.setRun({ id: runId, status: AssistantRunStatus.Running, step: 0, stepsHard: 8, escalated: false });

    return { runId, messageId: message.id };
  }

  public async Run(id: string): Promise<AssistantRun> {
    this.failIfConfigured();

    return this.runs.get(id)
      ?? { id, status: AssistantRunStatus.Done, step: 1, stepsHard: 8, escalated: false };
  }

  public async Stop(id: string): Promise<void> {
    this.failIfConfigured();
    this.stopped.push(id);
    const run = this.runs.get(id);
    if (run) this.setRun({ ...run, status: AssistantRunStatus.Stopped });
  }

  public async Trace(): Promise<AssistantTrace> {
    this.failIfConfigured();

    return this.trace;
  }

  public async Clear(): Promise<void> {
    this.failIfConfigured();
    this.cleared++;
    this.history = { messages: [] };
  }

  public async Memory(): Promise<AssistantMemory> {
    this.failIfConfigured();

    return { shared: [...this.memory.shared], personal: [...this.memory.personal] };
  }

  /** Чужой и несуществующий факт — `404`, как на сервере */
  public async DeleteFact(id: string): Promise<void> {
    this.failIfConfigured();

    const exists = [...this.memory.shared, ...this.memory.personal].some(x => x.id === id);
    if (!exists) throw serverRefusal(404, 'Mock: fact not found');

    this.forgotten.push(id);
    this.memory = {
      shared: this.memory.shared.filter(x => x.id !== id),
      personal: this.memory.personal.filter(x => x.id !== id)
    };
  }

  /** Пустая запись и запись длиннее минуты — отказ, как на сервере */
  public async Transcribe(audio: Blob, _fileName: string, seconds: number): Promise<string> {
    this.failIfConfigured();

    if (this.transcribeRefusal != null) {
      const { code, workspace } = this.transcribeRefusal;
      throw serverRefusal(400, `Mock: ${code}`, `assistant.error.${code}`,
        code.endsWith('LimitReached') ? { workspace } : undefined);
    }
    if (audio.size === 0) throw serverRefusal(400, 'Mock: empty', 'assistant.error.transcriptionFailed');
    if (seconds > MAX_SECONDS) throw serverRefusal(400, 'Mock: too long', 'assistant.error.audioTooLong');

    this.transcribed.push(seconds);

    return this.transcription;
  }

  private failIfConfigured() {
    if (!this.failure) return;

    const message = this.failure;
    this.failure = null;
    throw serverRefusal(500, message);
  }
}
