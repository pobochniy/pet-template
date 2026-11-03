import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { AssistantComponent } from './assistant.component';
import { AssistantService } from './assistant.service';
import { AssistantApiService } from '../shared/api/assistant-api.service';
import { AssistantApiServiceMock } from '../shared/api/assistant-api.service.mock';
import { I18nService } from '../shared/i18n/i18n.service';
import { loadAllScopes } from '../shared/i18n/i18n.testing';
import { AlertsService } from '../shared/alerts/alerts.service';
import { WorkspaceContextService } from '../shared/core/workspace-context.service';
import { SessionResetService } from '../shared/core/session-reset.service';
import {
  AssistantFactKind, AssistantMessageRole, AssistantPlanStatus, AssistantRunStatus
} from '../shared/models/assistant.model';
import { assistantHistoryDto, assistantMessageDto, assistantPlanDto, assistantRunDto } from '../shared/api/dto.mock';

/**
 * Экран чата (спец. assistant, 2, 3 и 14). Сети здесь нет: ответы ассистента
 * задаёт мок, как в жизни их задаёт модель
 */
describe('AssistantComponent', () => {
  let fixture: ComponentFixture<AssistantComponent>;
  let component: AssistantComponent;
  let api: AssistantApiServiceMock;
  let workspace: WorkspaceContextService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        AssistantApiServiceMock,
        { provide: AssistantApiService, useExisting: AssistantApiServiceMock }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(AssistantApiServiceMock);
    workspace = TestBed.inject(WorkspaceContextService);
    workspace.setCurrentWorkspaceId('ws-ivanovy');
  });

  afterEach(() => workspace.setCurrentWorkspaceId(null));

  /** Новый экран на каждый тест: состояние ассистента живёт в сервисе-синглтоне */
  async function create() {
    fixture = TestBed.createComponent(AssistantComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const element = () => fixture.nativeElement as HTMLElement;

  it('should offer hints in an empty conversation', async () => {
    await create();

    const hints = element().querySelectorAll('.assistant__empty button');
    expect(hints.length).toBe(3);
    expect(hints[0].textContent).toContain('как пригласить коллегу в пространство');
  });

  it('should put the message into the feed at once: the person has already written it', async () => {
    await create();

    await component.send('как сменить язык');

    expect(api.sent).toEqual(['как сменить язык']);
    expect(TestBed.inject(AssistantService).messages()[0].content).toBe('как сменить язык');
  });

  it('should show the answer after the run is over', async () => {
    await create();
    api.setHistory(assistantHistoryDto({
      messages: [assistantMessageDto({
        id: 'answer',
        role: AssistantMessageRole.Assistant,
        content: 'Откройте страницу участников.',
        runId: 'run-1',
        createdAtUtc: new Date().toISOString()
      })]
    }));
    api.setRun(assistantRunDto({ id: 'run-1', status: AssistantRunStatus.Done, step: 1, stepsHard: 8, escalated: false }));

    await component.send('как пригласить коллегу');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element().textContent).toContain('Откройте страницу участников.');
    expect(element().querySelector('app-assistant-trace')).not.toBeNull();
    expect(TestBed.inject(AssistantService).running()).toBe(false);
  });

  /** Ответ — markdown узлами: ссылка на свою страницу — переход, чужая и разметка HTML — текст (2.2, 9) */
  it('should render the answer as markdown with links only to own pages', async () => {
    await create();
    api.setHistory(assistantHistoryDto({
      messages: [assistantMessageDto({
        id: 'answer',
        role: AssistantMessageRole.Assistant,
        content: 'Откройте **[участников](/workspace/members)**:\n- *шаг* один\n- [чужой](https://evil.example) <img src=x onerror=alert(1)>',
        runId: 'run-1'
      })]
    }));
    api.setRun(assistantRunDto({ id: 'run-1', status: AssistantRunStatus.Done, step: 1 }));

    await component.send('как пригласить коллегу');
    await fixture.whenStable();
    fixture.detectChanges();

    const answer = element().querySelector('.assistant__answer') as HTMLElement;
    expect(answer.querySelector('strong a')?.getAttribute('href')).toBe('/workspace/members');
    expect(answer.querySelectorAll('li').length).toBe(2);
    expect(answer.querySelector('li em')?.textContent).toBe('шаг');
    expect(answer.querySelectorAll('a').length).toBe(1);
    expect(answer.querySelector('img')).toBeNull();
    expect(answer.textContent).toContain('чужой (https://evil.example) <img src=x onerror=alert(1)>');
  });

  it('should keep own message as plain text, not markdown', async () => {
    await create();

    await component.send('**не жирный**');
    await fixture.whenStable();
    fixture.detectChanges();

    const mine = element().querySelector('.assistant__bubble--mine') as HTMLElement;
    expect(mine.querySelector('strong')).toBeNull();
    expect(mine.textContent).toContain('**не жирный**');
  });

  /** Плашка хода: подпись инструмента — из словаря по имени, а не от модели (раздел 3) */
  it('should label the current activity from the dictionary and show the step after escalation', async () => {
    await create();
    api.setRun(assistantRunDto({
      id: 'run-1', status: AssistantRunStatus.Running, step: 9, stepsHard: 30, escalated: true,
      currentActivity: 'search_help'
    }));

    await component.send('расскажи про пространства');
    await fixture.whenStable();
    fixture.detectChanges();

    const status = element().querySelector('.assistant__status') as HTMLElement;
    expect(status.textContent).toContain('ищу в справке');
    expect(status.textContent).toContain('шаг 9 из 30');
    component.ngOnDestroy();
  });

  it('should render the plan block from the plan, not from the prose', async () => {
    api.setHistory(assistantHistoryDto({
      messages: [assistantMessageDto({
        id: 'plan',
        role: AssistantMessageRole.Assistant,
        content: 'Делаем?',
        runId: 'run-0',
        plan: assistantPlanDto({
          id: 'p1',
          status: AssistantPlanStatus.Pending,
          operations: { operations: [{ index: 1, kind: 'CreateNote', args: { title: 'Шарлотка' } }] }
        }),
        createdAtUtc: new Date().toISOString()
      })]
    }));

    await create();

    expect(element().querySelector('app-plan-block')?.textContent).toContain('Шарлотка');
  });

  it('should send Enter and keep Shift+Enter as a line break', async () => {
    await create();
    component.draft.set('да');

    component.onKeydown(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true }));
    expect(component.draft()).toBe('да');

    component.onKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));
    await fixture.whenStable();
    expect(api.sent).toEqual(['да']);
    expect(component.draft()).toBe('');
  });

  it('should offer «Stop» only from the tenth second', async () => {
    vi.useFakeTimers();
    try {
      await create();
      api.setRun(assistantRunDto({ id: 'run-1', status: AssistantRunStatus.Running, step: 1, stepsHard: 8, escalated: false }));

      await component.send('расскажи про права');
      expect(component.canStop()).toBe(false);

      vi.advanceTimersByTime(10_000);
      expect(component.canStop()).toBe(true);
      component.ngOnDestroy();
    } finally {
      vi.useRealTimers();
    }
  });

  it('should send the option as the same reply the person would type', async () => {
    api.setHistory(assistantHistoryDto({
      messages: [assistantMessageDto({
        id: 'question',
        role: AssistantMessageRole.Assistant,
        content: 'Какое пространство?',
        runId: 'run-0',
        options: ['Семья', 'Работа'],
        createdAtUtc: new Date().toISOString()
      })]
    }));
    await create();

    const option = Array.from(element().querySelectorAll<HTMLButtonElement>('.assistant__options button'))
      .find(x => x.textContent?.trim() === 'Работа');
    option?.click();
    await fixture.whenStable();

    expect(api.sent).toEqual(['Работа']);
  });

  /**
   * «Отправить» — единственный способ отправки на телефоне: Enter там переносит строку.
   * Поэтому — настоящим кликом: сломанная привязка видна только так. Слушатель на форме
   * ловит `defaultPrevented` и не даёт браузеру уйти на перезагрузку
   */
  it('should send by the button without reloading the page', async () => {
    await create();

    const textarea = element().querySelector('.assistant__input textarea') as HTMLTextAreaElement;
    textarea.value = 'как удалить аккаунт';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = element().querySelector('.assistant__input') as HTMLFormElement;
    let prevented: boolean | null = null;
    form.addEventListener('submit', (event: Event) => {
      prevented = event.defaultPrevented;
      event.preventDefault();
    });

    (element().querySelector('.assistant__input button[type="submit"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(api.sent).toEqual(['как удалить аккаунт']);
    expect(prevented, 'отправку формы браузеру отдавать нельзя — это перезагрузка').toBe(true);
  });

  it('should ask before clearing and clear on «Erase»', async () => {
    api.setHistory(assistantHistoryDto({
      messages: [assistantMessageDto({ id: 'm1', role: AssistantMessageRole.User, content: 'привет', createdAtUtc: new Date().toISOString() })]
    }));
    await create();

    (element().querySelector('#assistant-clear') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(api.cleared).toBe(0);

    (element().querySelector('#assistant-clear-yes') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(api.cleared).toBe(1);
    expect(TestBed.inject(AssistantService).messages()).toEqual([]);
  });

  /** Все модели разомкнуты — отказ кодом на входе, всплывающей ошибкой (спец. assistant, 14) */
  it('should say the assistant is unavailable and keep the input', async () => {
    await create();
    api.refuseSend('modelUnavailable');

    await component.send('привет');
    fixture.detectChanges();

    expect(TestBed.inject(AlertsService).alerts().map(x => x.content))
      .toContain('Ассистент сейчас недоступен, попробуйте позже');
    expect(element().querySelector('form.assistant__input')).not.toBeNull();
    expect(TestBed.inject(AssistantService).messages()).toEqual([]);
  });

  /** Лимит пространства (13.6): отправить нельзя, читать — можно всё */
  describe('workspace limit', () => {
    beforeEach(() => {
      api.setHistory(assistantHistoryDto({
        messages: [assistantMessageDto({
          id: 'old-answer',
          role: AssistantMessageRole.Assistant,
          content: 'Откройте страницу участников.',
          createdAtUtc: new Date().toISOString()
        })]
      }));
      api.refuseSend('dailyLimitReached', 'Семья Ивановых');
    });

    it('should put the plate in place of the input, name the space and say the limit is shared', async () => {
      await create();

      await component.send('как пригласить коллегу');
      fixture.detectChanges();

      expect(element().querySelector('form.assistant__input')).toBeNull();
      const plate = element().querySelector('app-limit-reached') as HTMLElement;
      expect(plate.textContent).toContain('«Семья Ивановых»');
      expect(plate.textContent).toContain('дневной лимит');
      expect(plate.textContent).toContain('общий для всех участников');
    });

    it('should keep the feed readable and drop the unsent message, without a second alert', async () => {
      await create();

      await component.send('как пригласить коллегу');

      expect(TestBed.inject(AssistantService).messages().map(x => x.content)).toEqual(['Откройте страницу участников.']);
      expect(TestBed.inject(AlertsService).alerts()).toEqual([]);
    });

    it('should bring the input back on «Got it»', async () => {
      await create();
      await component.send('как пригласить коллегу');
      fixture.detectChanges();

      (element().querySelector('.limit-reached__dismiss') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(element().querySelector('form.assistant__input')).not.toBeNull();
    });

    it('should not carry the plate to the next user in the same tab', async () => {
      await create();
      await component.send('как пригласить коллегу');

      await TestBed.inject(SessionResetService).run();

      const assistant = TestBed.inject(AssistantService);
      expect(assistant.limitReached()).toBe(false);
      expect(assistant.messages()).toEqual([]);
    });
  });

  /** Личный лимит человека (13.6): та же плашка, но без пространства — лимит общий на все его */
  describe('user limit', () => {
    beforeEach(() => api.refuseSend('userDailyLimitReached'));

    it('should put the plate in place of the input and say the limit spans all own spaces', async () => {
      await create();

      await component.send('как пригласить коллегу');
      fixture.detectChanges();

      expect(element().querySelector('form.assistant__input')).toBeNull();
      const plate = element().querySelector('app-limit-reached') as HTMLElement;
      expect(plate.textContent).toContain('ваш дневной лимит');
      expect(plate.textContent).toContain('всех ваших пространств');
    });
  });

  /** Тред — пара «человек + пространство» (12.1): другое пространство — другой разговор */
  it('should show the other space own thread after switching', async () => {
    api.setHistory(assistantHistoryDto({
      messages: [assistantMessageDto({ id: 'm1', role: AssistantMessageRole.User, content: 'вопрос в семье', createdAtUtc: new Date().toISOString() })]
    }));
    await create();
    expect(element().textContent).toContain('вопрос в семье');

    api.setHistory(assistantHistoryDto({
      messages: [assistantMessageDto({ id: 'm2', role: AssistantMessageRole.User, content: 'вопрос на работе', createdAtUtc: new Date().toISOString() })]
    }));
    workspace.setCurrentWorkspaceId('ws-rabota');
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element().textContent).not.toContain('вопрос в семье');
    expect(element().textContent).toContain('вопрос на работе');
  });

  /** Память (12.5): факт пишется мимо плана, поэтому виден строкой и убирается отсюда же */
  describe('remembered facts', () => {
    const fact = {
      id: 'fact-1', kind: AssistantFactKind.Alias, text: '«Илья» — это Илья Петров',
      personal: false, createdAtUtc: new Date().toISOString(), sourceRunId: 'run-1'
    };

    beforeEach(() => {
      api.setMemory({ shared: [fact], personal: [] });
      api.setHistory(assistantHistoryDto({
        messages: [assistantMessageDto({
          id: 'answer', role: AssistantMessageRole.Assistant, content: 'Хорошо.', runId: 'run-1',
          facts: [fact], createdAtUtc: new Date().toISOString()
        })]
      }));
    });

    it('should show the remembered fact as a line under the answer', async () => {
      await create();

      expect(element().querySelector('.assistant__fact')?.textContent).toContain('Запомнил: «Илья» — это Илья Петров');
    });

    it('should remove the fact only after confirmation, keeping the message', async () => {
      await create();

      (element().querySelector('.fact-forget') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(api.forgotten).toEqual([]);
      expect(element().querySelector('.assistant__fact')?.textContent).toContain('Убрать этот факт?');

      (element().querySelector('.fact-forget-confirm') as HTMLButtonElement).click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(api.forgotten).toEqual(['fact-1']);
      expect(element().querySelector('.assistant__fact')).toBeNull();
      expect(element().textContent).toContain('Хорошо.');
    });
  });

  /** Голос заполняет поле, а не отправляет (6) */
  it('should append the transcription to the draft without sending it', async () => {
    await create();
    component.draft.set('напомни');

    component.onVoice('как сменить язык');

    expect(component.draft()).toBe('напомни как сменить язык');
    expect(api.sent).toEqual([]);
  });
});
