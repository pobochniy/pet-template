import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { AssistantApiService } from '../shared/api/assistant-api.service';
import { AlertsService } from '../shared/alerts/alerts.service';
import { I18nService } from '../shared/i18n/i18n.service';
import { serverErrorMessage } from '../shared/core/server-error.util';
import { WorkspaceContextService } from '../shared/core/workspace-context.service';
import { SessionResetService } from '../shared/core/session-reset.service';
import {
  AssistantMessage, AssistantMessageRole, AssistantRun, isRunFinished
} from '../shared/models/assistant.model';

/** Сколько держится всплывающая ошибка ассистента */
const ALERT_MS = 5000;

/** Отказы `Send` лимитом пространства (спец. assistant, 13.6) — общий лимит всех участников */
export const LIMIT_REFUSAL_CODES: readonly string[] = [
  'assistant.error.dailyLimitReached',
  'assistant.error.monthlyLimitReached'
];

/**
 * Состояние чата с ассистентом (спец. assistant, разделы 2, 3 и 14).
 *
 * Узнать, что ход закончился, можно только спросив: уведомлений в продукте нет,
 * стрима токенов нет тоже. Поэтому здесь живёт цикл долгого опроса — один запрос
 * на двадцать секунд вместо двадцати запросов в секунду.
 *
 * Тред — пара «человек + пространство» (12.1): сменили пространство в шапке —
 * лента, ход и плашка лимита прошлого пространства уходят, открытый экран
 * перечитывает свой тред. Выход из аккаунта убирает всё через `SessionResetService`.
 */
@Injectable({ providedIn: 'root' })
export class AssistantService {
  private readonly api = inject(AssistantApiService);
  private readonly alerts = inject(AlertsService);
  private readonly i18n = inject(I18nService);
  private readonly workspaceContext = inject(WorkspaceContextService);

  /** Сколько секунд сервер держит ответ. Своё ожидание у него тоже ограничено */
  private static readonly WaitSeconds = 20;

  /** Пауза после сетевой ошибки: сеть моргнула — не повод бросать ход */
  private static readonly RetryMs = 3000;

  /**
   * Зазор между опросами. В бою сервер держит ответ сам, и зазор не нужен вовсе;
   * он страхует от обратного случая — ответа «сразу же», который без паузы
   * превратил бы долгий опрос в горячий цикл
   */
  private static readonly PollGapMs = 250;

  public readonly messages = signal<AssistantMessage[]>([]);
  public readonly activeRun = signal<AssistantRun | undefined>(undefined);
  public readonly nextCursor = signal<string | undefined>(undefined);
  public readonly loading = signal(false);

  /** Идёт ли ход прямо сейчас — от этого зависит плашка хода в ленте */
  public readonly running = computed(() => !isRunFinished(this.activeRun()));

  /** Есть ли что подгружать выше */
  public readonly hasMore = computed(() => this.nextCursor() != null);

  /**
   * Лимит пространства исчерпан (13.6). Ставится **по отказу сервера** — источник
   * истины ответ `Send`, своего счётчика у клиента нет. Помнится по пространству:
   * лимит у каждого свой, и в другом пространстве строка ввода на месте, пока свой
   * `Send` не откажет.
   *
   * Лента при этом читается целиком — исчезает только возможность отправить.
   * Человек может закрыть плашку сам (`dismissLimit`): дневной лимит откроется
   * завтра, и держать плашку до перезагрузки было бы враньём
   */
  private readonly limit = signal<{ workspaceId: string | null; text: string } | null>(null);

  public readonly limitReached = computed(() =>
    this.limit()?.workspaceId === this.workspaceContext.currentWorkspaceId());

  /** Текст плашки — перевод отказа с названием пространства, которое прислал сам отказ */
  public readonly limitText = computed(() => (this.limitReached() ? this.limit()?.text ?? '' : ''));

  private watching: string | null = null;

  /** Открыт ли экран чата: только ему нужна лента после смены пространства */
  private attached = false;

  constructor() {
    // Следующий в той же вкладке не должен унаследовать ни плашку, ни чужую ленту
    inject(SessionResetService).register(() => this.reset());

    // Своё пространство — свой тред (12.1): чужая лента не должна мелькнуть ни на миг
    let first = true;
    effect(() => {
      this.workspaceContext.currentWorkspaceId();
      if (first) {
        first = false;

        return;
      }

      untracked(() => {
        this.reset();
        if (this.attached) void this.load();
      });
    });

    // Вкладку свернули — ждать незачем; вернулись — спрашиваем сразу (раздел 14).
    // Слушатель один на приложение: сервис живёт столько же
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => {
        if (document.hidden) return;

        const run = this.activeRun();
        if (run && !isRunFinished(run)) void this.watch(run.id);
      });
    }
  }

  /** Экран чата открыт: лента и живой ход, если он есть */
  public async open() {
    this.attached = true;
    await this.load();
  }

  /**
   * Экран закрыли — следить больше некому. Сам ход при этом продолжается:
   * он живёт в очереди, а не в этом запросе (раздел 3)
   */
  public close() {
    this.attached = false;
    this.watching = null;
  }

  /** Последняя страница ленты и живой ход, если он есть */
  public async load() {
    this.loading.set(true);
    try {
      const history = await this.api.History();
      this.messages.set(visible(history.messages));
      this.nextCursor.set(history.nextCursor ?? undefined);
      this.activeRun.set(history.activeRun ?? undefined);

      // Ход мог начаться до того, как человек вернулся на экран
      if (history.activeRun) void this.watch(history.activeRun.id);
    } catch (e) {
      this.fail(e);
    } finally {
      this.loading.set(false);
    }
  }

  /** Страница выше — дописывается сверху, как в мессенджере */
  public async loadMore() {
    const cursor = this.nextCursor();
    if (!cursor || this.loading()) return;

    this.loading.set(true);
    try {
      const history = await this.api.History(cursor);
      this.messages.update(current => [...visible(history.messages), ...current]);
      this.nextCursor.set(history.nextCursor ?? undefined);
    } catch (e) {
      this.fail(e);
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Реплика человека. Она появляется в ленте сразу, не дожидаясь сервера:
   * своё сообщение человек уже написал, и показывать его после круга
   * по сети незачем.
   *
   * Поле ввода при живом ходе не блокируется: сообщение просто встанет
   * в очередь треда (раздел 3)
   */
  public async send(text: string) {
    const trimmed = text.trim();
    if (trimmed.length === 0) return;

    const optimistic: AssistantMessage = {
      id: `local-${Date.now()}`,
      role: AssistantMessageRole.User,
      content: trimmed,
      createdAtUtc: new Date().toISOString()
    };
    this.messages.update(current => [...current, optimistic]);

    try {
      const sent = await this.api.Send(trimmed);
      void this.watch(sent.runId);
    } catch (e) {
      // Не ушло — убираем своё же сообщение: пусть лучше человек отправит заново,
      // чем увидит в ленте то, чего на сервере нет
      this.messages.update(current => current.filter(x => x.id !== optimistic.id));

      // Лимит — не поломка: ответом на него встаёт плашка на месте поля ввода,
      // а всплывающая ошибка сказала бы то же самое второй раз
      if (this.refusedByLimit(e)) return;

      // «Ассистент недоступен» и прочее — всплывающей ошибкой: поле остаётся, через
      // минуту модель может ожить
      this.fail(e);
    }
  }

  /**
   * Отказ лимитом пространства (13.6) — от `Send` или от расшифровки голоса: голос
   * проверяет тот же лимит до расшифровки (6). Ответ один — плашка на месте поля ввода.
   * `false` — отказ не про лимит, его показывает вызывающий
   */
  public refusedByLimit(e: unknown): boolean {
    if (!LIMIT_REFUSAL_CODES.includes(refusalCode(e) ?? '')) return false;

    this.limit.set({
      workspaceId: this.workspaceContext.getCurrentWorkspaceId(),
      text: serverErrorMessage(e, this.i18n, this.i18n.t('assistant.limit.title'))
    });

    return true;
  }

  /**
   * «Убрать» у записанного факта (12.5). Строка «Запомнил» уходит из сообщения,
   * само сообщение остаётся в ленте
   */
  public async removeFact(id: string) {
    try {
      await this.api.DeleteFact(id);
      this.messages.update(current => current.map(message => message.facts?.some(x => x.id === id)
        ? { ...message, facts: message.facts.filter(x => x.id !== id) }
        : message));
    } catch (e) {
      this.fail(e);
    }
  }

  /** «Понятно» на плашке лимита — строка ввода возвращается */
  public dismissLimit() {
    this.limit.set(null);
  }

  /**
   * Долгий опрос хода. Крутится, пока ход жив; кончился — подтягивает ленту
   * целиком и сливает по идентификаторам: сообщений за ход может появиться
   * несколько
   */
  public async watch(runId: string) {
    if (this.watching === runId) return;
    this.watching = runId;

    // Первый вопрос — без ожидания: плашка хода нужна сразу, а долгий опрос
    // ответил бы только на смене шага
    let wait = 0;

    try {
      while (this.watching === runId) {
        // Скрытая вкладка ход не возобновляет: вернулись — спросит `visibilitychange`
        if (typeof document !== 'undefined' && document.hidden) return;

        let run: AssistantRun;
        try {
          run = await this.api.Run(runId, wait);
          wait = AssistantService.WaitSeconds;
        } catch {
          // Сеть моргнула — ждём и пробуем снова. Ход при этом продолжается
          // на сервере: он живёт в очереди, а не в этом запросе
          await pause(AssistantService.RetryMs);
          continue;
        }

        // Пока ждали ответа, сменили пространство или вышли — ответ уже не про этот тред
        if (this.watching !== runId) return;

        this.activeRun.set(run);
        if (isRunFinished(run)) {
          await this.refresh();

          return;
        }

        await pause(AssistantService.PollGapMs);
      }
    } finally {
      if (this.watching === runId) this.watching = null;
    }
  }

  /**
   * «Остановить»: сделанное по уже сказанному «да» остаётся, а в ленту приходит
   * сообщение с тем, что успел (раздел 3)
   */
  public async stop() {
    const run = this.activeRun();
    if (!run) return;

    try {
      await this.api.Stop(run.id);
    } catch (e) {
      this.fail(e);
    }
  }

  /**
   * «Очистить»: стирает переписку и трассы (12.1.1). Подтверждение спрашивает
   * экран, не сервис
   */
  public async clear() {
    try {
      await this.api.Clear();
      this.messages.set([]);
      this.nextCursor.set(undefined);
      this.activeRun.set(undefined);
      this.watching = null;
    } catch (e) {
      this.fail(e);
    }
  }

  /**
   * Лента после хода. Берём последнюю страницу и приставляем её к тому, что уже
   * подгружено выше: свои оптимистичные сообщения при этом уходят — они вернулись
   * с сервера с настоящими идентификаторами
   */
  private async refresh() {
    try {
      const history = await this.api.History();
      const page = visible(history.messages);
      const ids = new Set(page.map(x => x.id));
      const older = this.messages().filter(x => !ids.has(x.id) && !x.id.startsWith('local-'));

      this.messages.set([...older, ...page]);
      this.nextCursor.set(history.nextCursor ?? undefined);
      this.activeRun.set(history.activeRun ?? undefined);

      // Пока шёл этот ход, в очереди треда ждала следующая реплика — следим и за ней
      if (history.activeRun) void this.watch(history.activeRun.id);
    } catch (e) {
      this.fail(e);
    }
  }

  private reset() {
    this.watching = null;
    this.messages.set([]);
    this.activeRun.set(undefined);
    this.nextCursor.set(undefined);
    this.limit.set(null);
  }

  private fail(e: unknown) {
    this.alerts.push('danger', serverErrorMessage(e, this.i18n, this.i18n.t('assistant.error.unknown')), ALERT_MS);
  }
}

/** Резюме компакции в ленте не показывается — оно для модели (12.4) */
function visible(messages: AssistantMessage[]) {
  return messages.filter(x => x.role !== AssistantMessageRole.Summary);
}

function refusalCode(error: unknown): string | undefined {
  const code = (error as { error?: { code?: unknown } } | null)?.error?.code;

  return typeof code === 'string' ? code : undefined;
}

function pause(ms: number) {
  return new Promise<void>(resolve => setTimeout(resolve, ms));
}
