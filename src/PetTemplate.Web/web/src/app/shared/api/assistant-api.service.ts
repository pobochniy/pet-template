import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BaseApiService } from './base-api.service';
import {
  AssistantHistory, AssistantMemory, AssistantRun, AssistantSendResult, AssistantTrace
} from '../models/assistant.model';

/** Заголовок зоны — `AssistantController` читает его на `Send` (спец. assistant, 10) */
export const TIME_ZONE_HEADER = 'X-Time-Zone';

export interface IAssistantApiService {
  History(cursor?: string, signal?: AbortSignal): Promise<AssistantHistory>;
  Send(text: string): Promise<AssistantSendResult>;
  Run(id: string, waitSeconds?: number, signal?: AbortSignal): Promise<AssistantRun>;
  Stop(id: string): Promise<void>;
  Trace(id: string): Promise<AssistantTrace>;
  Clear(): Promise<void>;
  Memory(signal?: AbortSignal): Promise<AssistantMemory>;
  DeleteFact(id: string): Promise<void>;
  Transcribe(audio: Blob, fileName: string, seconds: number): Promise<string>;
}

/**
 * Ассистент (спец. assistant, раздел 14). Пространство ставит перехватчик —
 * в адресах его нет.
 *
 * Стрима токенов здесь нет намеренно: `EventSource` не умеет заголовки, а у нас
 * и JWT, и `X-Workspace-Id` едут ими. Вместо него — долгий опрос `Run(id, wait)`:
 * тот же `HttpClient`, те же перехватчики, нового транспорта нет.
 */
@Injectable({ providedIn: 'root' })
export class AssistantApiService extends BaseApiService implements IAssistantApiService {
  constructor(http: HttpClient) {
    super('Assistant', http);
  }

  /** Последняя страница ленты; `cursor` — идентификатор верхнего сообщения страницы */
  public History(cursor?: string, signal?: AbortSignal) {
    return this.get<AssistantHistory>(cursor ? `History?cursor=${encodeURIComponent(cursor)}` : 'History', signal);
  }

  /**
   * Реплика человека. Ход возвращается всегда — даже одношаговый идёт командой
   * в очереди (раздел 3).
   *
   * Зона устройства едет заголовком только здесь, а не перехватчиком на все запросы:
   * её читает один `Send` — прогон запоминает её на весь ход (раздел 10), — а
   * перехватчик лежал бы в начальном бандле ради одного запроса ленивого экрана
   * (`tech-debt/assistant-timezone.md`)
   */
  public Send(text: string) {
    return firstValueFrom(this.http.post<AssistantSendResult>('/api/Assistant/Send', { text }, {
      headers: { [TIME_ZONE_HEADER]: Intl.DateTimeFormat().resolvedOptions().timeZone }
    }));
  }

  /**
   * Состояние хода. С `waitSeconds` сервер держит запрос до смены состояния —
   * короткий ход отвечает в момент завершения, а не на следующем тике.
   * `signal` обрывает ожидание: экран закрыт или пространство сменилось
   */
  public Run(id: string, waitSeconds = 0, signal?: AbortSignal) {
    return this.get<AssistantRun>(waitSeconds > 0 ? `Run/${id}?wait=${waitSeconds}` : `Run/${id}`, signal);
  }

  public async Stop(id: string) {
    await this.post<void>(`Run/${id}/Stop`);
  }

  /** «Показать ход мысли» (13.2) */
  public Trace(id: string) {
    return this.get<AssistantTrace>(`Run/${id}/Trace`);
  }

  /** Стирает переписку и трассы; прогоны, планы и расход остаются (12.1.1) */
  public async Clear() {
    await this.delete<void>('Clear');
  }

  /** Память ассистента в активном пространстве: общее и своё (12.5) */
  public Memory(signal?: AbortSignal) {
    return this.get<AssistantMemory>('Memory', signal);
  }

  /** Убрать факт — насовсем; строка «Запомнил» в ленте при этом остаётся (12.5) */
  public async DeleteFact(id: string) {
    await this.delete<void>(`Memory/${id}`);
  }

  /**
   * Голос → текст (6.1). Расшифровка возвращает текст в поле ввода, а не отправляет
   * его: транскрипция ошибается в именах и числах, а уходить в тред должно то,
   * что человек видел глазами.
   *
   * Тело — `multipart`: заголовок с границей ставит сам браузер, `Content-Type`
   * руками не задаём. Длительность едет рядом — сервер сверяет её с потолком
   */
  public async Transcribe(audio: Blob, fileName: string, seconds: number) {
    const form = new FormData();
    form.append('audio', audio, fileName);
    form.append('seconds', String(seconds));

    const result = await this.post<{ text: string }>('Transcribe', form);

    return result.text;
  }
}
