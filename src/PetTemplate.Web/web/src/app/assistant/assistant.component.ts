import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, inject, signal, untracked, viewChild
} from '@angular/core';
import Brain from 'lucide/dist/esm/icons/brain.mjs';
import SendHorizontal from 'lucide/dist/esm/icons/send-horizontal.mjs';
import { AssistantService } from './assistant.service';
import { ASSISTANT_TEXT_STATE } from './assistant-text';
import { ScopedKey } from '../shared/i18n/generated/i18n-keys';
import { PlanBlockComponent } from './plan-block/plan-block.component';
import { AssistantTraceComponent } from './trace/trace.component';
import { LimitReachedComponent } from './limit-reached/limit-reached.component';
import { VoiceInputComponent } from './voice/voice-input.component';
import { MarkdownComponent } from '../shared/markdown/markdown.component';
import { IconComponent } from '../shared/icons/icon.component';
import { AssistantMessage, AssistantMessageRole } from '../shared/models/assistant.model';
import { injectI18n } from '../shared/i18n/inject-i18n';
import { ASSISTANT_SCOPES } from './assistant.scopes';

/**
 * Чат с ассистентом (спец. assistant, 2) — привычный мессенджер: лента,
 * своё справа, ответы слева, поле ввода внизу.
 *
 * <b>Кнопок управления диалогом в ленте нет.</b> Согласие, отказ и правка — обычные
 * реплики (5.1). Кликается здесь «Показать ход мысли» (13.2) и варианты ответа
 * у уточняющего вопроса (5.2) — они отправляют ту же реплику, что человек набрал бы сам.
 */
@Component({
  selector: 'app-assistant',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlanBlockComponent, AssistantTraceComponent, LimitReachedComponent, VoiceInputComponent, MarkdownComponent, IconComponent
  ],
  templateUrl: './assistant.component.html',
  styleUrl: './assistant.component.css'
})
export class AssistantComponent implements AfterViewInit, OnDestroy {
  protected readonly icons = { Brain, SendHorizontal };

  protected readonly i18n = injectI18n(...ASSISTANT_SCOPES);
  public readonly assistant = inject(AssistantService);

  /** Подсказки пустого состояния — вопросы, на которые справка ядра отвечает */
  public readonly hints: readonly ScopedKey<'assistant'>[] = [
    'assistant.empty.hint1',
    'assistant.empty.hint2',
    'assistant.empty.hint3'
  ];

  /**
   * «Остановить» появляется с десятой секунды ожидания, независимо от номера шага:
   * сидеть перед индикатором без выхода нельзя. Это правило интерфейса,
   * а не предохранитель (раздел 3)
   */
  private static readonly StopAfterMs = 10_000;

  public readonly draft = signal('');
  public readonly confirmingClear = signal(false);
  public readonly canStop = signal(false);

  /** Факт, удаление которого ждёт подтверждения в строке «Запомнил» (12.5) */
  public readonly forgetting = signal<string | null>(null);

  private readonly feed = viewChild<ElementRef<HTMLElement>>('feed');
  private readonly top = viewChild<ElementRef<HTMLElement>>('top');

  private stopTimer: ReturnType<typeof setTimeout> | null = null;
  private observer: IntersectionObserver | null = null;

  constructor() {
    // Лента прокручивается к последнему: новое всегда внизу
    effect(() => {
      this.assistant.messages();
      queueMicrotask(() => this.scrollToBottom());
    });

    // Ход кончился — «Остановить» больше не нужна; ход идёт (вернулись на экран
    // посреди хода) — кнопка появится через те же десять секунд
    effect(() => {
      const running = this.assistant.running();
      untracked(() => {
        if (!running) this.clearStopTimer();
        else if (this.stopTimer == null && !this.canStop()) this.startStopTimer();
      });
    });

    void this.start();
  }

  public ngAfterViewInit() {
    const marker = this.top()?.nativeElement;
    if (!marker || typeof IntersectionObserver === 'undefined') return;

    // Подгрузка начинается за экран до края, кнопки «показать ещё» нет (раздел 14)
    this.observer = new IntersectionObserver(entries => {
      if (entries.some(x => x.isIntersecting) && this.assistant.messages().length > 0) void this.assistant.loadMore();
    }, { rootMargin: '200px' });
    this.observer.observe(marker);
  }

  public ngOnDestroy() {
    this.observer?.disconnect();
    this.clearStopTimer();
    // Экран закрыли — следить больше некому. Ход при этом не обрывается:
    // он идёт в очереди и допишет ответ в ленту (раздел 3)
    this.assistant.close();
  }

  public isMine(message: AssistantMessage) {
    return message.role === AssistantMessageRole.User;
  }

  /** «Шаг 7 из 30» — только после эскалации: короткому ходу номер шага ни к чему (раздел 3) */
  public stepLabel() {
    const run = this.assistant.activeRun();

    return this.i18n.t('assistant.step', { step: run?.step ?? 0, total: run?.stepsHard ?? 0 });
  }

  /**
   * Что ассистент делает сейчас — строка из словаря по имени инструмента,
   * а не сочинённая моделью (раздел 3). Инструмент раздела без строки
   * в словаре — общее «думаю»
   */
  public activityLabel() {
    const activity = this.assistant.activeRun()?.currentActivity;
    const label = activity ? this.i18n.translateCode(`assistant.activity.${activity}`) : null;

    return label ?? this.i18n.t('assistant.thinking');
  }

  public onInput(event: Event) {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  /** Enter отправляет, Shift+Enter переносит строку */
  public onKeydown(event: KeyboardEvent) {
    if (event.key !== 'Enter' || event.shiftKey || event.isComposing) return;

    event.preventDefault();
    this.submit();
  }

  /**
   * Отправка формы. <b>Действие браузера по умолчанию гасим здесь</b>: форма
   * без директивы форм отправляется сама — то есть уводит страницу на перезагрузку,
   * и набранное пропадает вместе с ней. Реактивные формы гасят его за счёт
   * `FormGroupDirective`, а этой форме гасить нечем — поле у неё не формное,
   * только сигнал `draft` (`@angular/forms` в чат ради одного поля не берём)
   */
  public submit(event?: Event) {
    event?.preventDefault();

    const text = this.draft();
    if (text.trim().length === 0) return;

    this.draft.set('');
    void this.send(text);
  }

  public async send(text: string) {
    this.clearStopTimer();
    this.startStopTimer();
    await this.assistant.send(text);
  }

  /**
   * Расшифровка дописывается в поле, а не отправляется (6): человек видит её, правит
   * и отправляет сам. Набранное раньше не теряется
   */
  public onVoice(text: string) {
    const current = this.draft().trimEnd();
    this.draft.set(current.length > 0 ? `${current} ${text}` : text);
  }

  public async forget(id: string) {
    this.forgetting.set(null);
    await this.assistant.removeFact(id);
  }

  public async stop() {
    await this.assistant.stop();
  }

  public async clear() {
    this.confirmingClear.set(false);
    await this.assistant.clear();
  }

  /**
   * Первый заход: лента и — если человек пришёл со строки на главной — его фраза
   * первым сообщением (2.1)
   */
  private async start() {
    await this.assistant.open();

    const text = typeof history !== 'undefined' ? history.state?.[ASSISTANT_TEXT_STATE] : null;
    if (typeof text === 'string' && text.trim().length > 0) {
      // Перезагрузка страницы не должна отправить фразу второй раз
      history.replaceState({ ...history.state, [ASSISTANT_TEXT_STATE]: null }, '');
      await this.send(text);
    }
  }

  private startStopTimer() {
    this.canStop.set(false);
    this.stopTimer = setTimeout(() => {
      this.stopTimer = null;
      this.canStop.set(true);
    }, AssistantComponent.StopAfterMs);
  }

  private clearStopTimer() {
    if (this.stopTimer) clearTimeout(this.stopTimer);
    this.stopTimer = null;
    this.canStop.set(false);
  }

  private scrollToBottom() {
    const feed = this.feed()?.nativeElement;
    if (feed) feed.scrollTop = feed.scrollHeight;
  }
}
