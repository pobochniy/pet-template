import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { DialogsComponent } from './dialogs.component';
import { AdminDialog, AdminDialogTrace, AdminEvaluations, AdminJob, AdminModel, AdminModels } from '../core/admin.model';
import { AssistantRunStatus, AssistantStopReason, DialogStopReason } from '../core/assistant-run-status';
import { AdminJobKind, AdminJobStatus } from '../core/admin-job';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

/** Разбор диалогов (спец. admin, 16.2) и автооценка (16.3) */
describe('DialogsComponent', () => {
  let httpMock: HttpTestingController;

  const models: AdminModels = { currency: 'RUB', models: [], transcription: null };

  const dialog: AdminDialog = {
    runId: 'run-1',
    startedUtc: '2026-09-28T14:02:00Z',
    finishedUtc: '2026-09-28T14:03:00Z',
    workspaceId: 'ws-1',
    workspaceName: 'Рога и Копыта',
    modelId: 'm-1',
    modelName: 'DeepSeek-V4.1-Flash',
    status: AssistantRunStatus.Done,
    stopReason: AssistantStopReason.Completed,
    steps: 7,
    tokensIn: 3000,
    tokensOut: 700,
    cost: 0.0365,
    clarifications: null,
    satisfaction: null,
    traceExpired: false
  };

  const old: AdminDialog = { ...dialog, runId: 'run-old', workspaceName: 'Дача', traceExpired: true };

  const trace: AdminDialogTrace = {
    dialog,
    expired: false,
    steps: [
      {
        step: 2,
        kind: 'tool',
        toolName: 'find_issues',
        summary: 'искал задачи: молоко',
        input: '{"text":"молоко"}',
        output: '3 найдено',
        durationMs: 12,
        tokensIn: 0,
        tokensOut: 0,
        error: null,
        createdUtc: '2026-09-28T14:02:10Z'
      }
    ],
    evaluation: null
  };

  function setup(run: string | null = null) {
    TestBed.configureTestingModule({
      imports: [DialogsComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(run ? { run } : {}) } } }
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function judge(id: string, patch: Partial<AdminModel> = {}): AdminModel {
    return {
      id,
      priority: 1,
      provider: 'deepseek',
      key: id,
      displayName: id,
      costIn: 0.3,
      costCached: 0.075,
      costOut: 0.5,
      isEnabled: true,
      isLocal: false,
      openUntilUtc: null,
      failureCount: 0,
      isUsed: false,
      updatedUtc: '2026-09-29T12:00:00Z',
      updatedBy: null,
      version: 1,
      ...patch
    };
  }

  function job(patch: Partial<AdminJob> = {}): AdminJob {
    return {
      id: 'job-1',
      kind: AdminJobKind.DialogEvaluation,
      modelId: 'local',
      modelName: 'Локальная 3090',
      startedBy: 'Oleg',
      createdUtc: '2026-10-01T10:00:00Z',
      finishedUtc: '2026-10-01T10:05:00Z',
      status: AdminJobStatus.Done,
      planned: 5,
      done: 4,
      skipped: 3,
      failed: 1,
      tokensIn: 8000,
      tokensCached: 0,
      tokensOut: 800,
      cost: 0.44,
      error: 'поставщик лёг',
      report: null,
      ...patch
    };
  }

  async function render(
    run: string | null = null,
    nextBefore: string | null = null,
    catalog: AdminModels = models,
    evaluations: AdminEvaluations = { maxBatchSize: 20, jobs: [] }
  ) {
    setup(run);
    const fixture = TestBed.createComponent(DialogsComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/Admin/Models').flush(catalog);
    httpMock.expectOne(r => r.url === '/api/Admin/Dialogs').flush({ dialogs: [dialog, old], nextBefore });
    httpMock.expectOne('/api/Admin/Dialogs/Evaluations').flush(evaluations);
    // Два ответа собираются через Promise.all — лишний круг, пока он развернётся
    await settle(fixture);
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('should list runs by signs without opening them', async () => {
    const { element } = await render();

    const rows = element.querySelectorAll('.dialog-row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Рога и Копыта');
    expect(rows[0].textContent).toContain('DeepSeek-V4.1-Flash');
    expect(rows[0].textContent).toContain('0,0365 RUB');
    expect(rows[1].textContent).toContain('содержимого уже нет');
    httpMock.expectNone(r => r.url.startsWith('/api/Admin/Dialogs/'));
  });

  it('should still list runs when the models do not load', async () => {
    setup();
    const fixture = TestBed.createComponent(DialogsComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/Admin/Models').flush(null, { status: 500, statusText: 'Server Error' });
    httpMock.expectOne(r => r.url === '/api/Admin/Dialogs').flush({ dialogs: [dialog, old], nextBefore: null });
    httpMock.expectOne('/api/Admin/Dialogs/Evaluations').flush({ maxBatchSize: 20, jobs: [] });
    await settle(fixture);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelectorAll('.dialog-row').length).toBe(2);
    expect(element.querySelector('.alert-danger')!.textContent).toContain('Модели не загрузились');
  });

  it('should ask for the last seven days by default', async () => {
    setup();
    const fixture = TestBed.createComponent(DialogsComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/Admin/Models').flush(models);

    const request = httpMock.expectOne(r => r.url === '/api/Admin/Dialogs');
    const from = new Date();
    from.setUTCDate(from.getUTCDate() - 6);
    expect(request.request.params.get('from')).toBe(from.toISOString().slice(0, 10));
    expect(request.request.params.has('to')).toBe(false);
    expect(request.request.params.has('maxSatisfaction')).toBe(false);
    request.flush({ dialogs: [], nextBefore: null });
    httpMock.expectOne('/api/Admin/Dialogs/Evaluations').flush({ maxBatchSize: 20, jobs: [] });
    await settle(fixture);
  });

  it('should offer the own card as the judge by default', async () => {
    const { element } = await render(null, null, {
      ...models,
      models: [judge('flash', { displayName: 'DeepSeek-V4.1-Flash' }), judge('local', { displayName: 'Локальная 3090', isLocal: true, isEnabled: false })]
    });

    const select = element.querySelector<HTMLSelectElement>('select[name="judge"]')!;
    expect(select.selectedOptions[0].textContent).toContain('Локальная 3090 · своя карта · выкл.');
  });

  it('should start an evaluation with the picked judge and batch size', async () => {
    const { fixture, element } = await render(null, null, { ...models, models: [judge('local', { isLocal: true })] });

    const size = element.querySelector<HTMLInputElement>('input[name="batchSize"]')!;
    size.value = '3';
    size.dispatchEvent(new Event('input'));
    await settle(fixture);
    element.querySelector('.evaluation form')!.dispatchEvent(new Event('submit'));
    const req = httpMock.expectOne('/api/Admin/Dialogs/Evaluations');

    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ modelId: 'local', batchSize: 3 });
    req.flush(job({ status: AdminJobStatus.Running, planned: 3, done: 1, skipped: 0, failed: 0, error: null }));
    await settle(fixture);

    expect(element.querySelector('.evaluation-row .job-state')!.textContent).toContain('идёт · 1 из 3');
  });

  it('should show what a finished evaluation judged, skipped and failed', async () => {
    const { element } = await render(null, null, models, { maxBatchSize: 20, jobs: [job()] });

    const cells = Array.from(element.querySelectorAll('.evaluation-row td')).map(x => x.textContent!.replace(/\s+/g, ' ').trim());
    expect(cells[3]).toBe('готово · поставщик лёг');
    expect(cells.slice(4)).toEqual(['4', '3', '1', '0,44 RUB']);
  });

  it('should not offer a second evaluation while one is running', async () => {
    const { element } = await render(null, null, { ...models, models: [judge('local')] }, {
      maxBatchSize: 20,
      jobs: [job({ status: AdminJobStatus.Running, finishedUtc: null })]
    });

    const evaluate = Array.from(element.querySelectorAll('button')).find(x => x.textContent!.trim() === 'Оценить')!;
    expect(evaluate.disabled).toBe(true);
  });

  it('should open a trace with what went in and came back', async () => {
    const { fixture, element } = await render();

    (element.querySelector('.dialog-row') as HTMLElement).click();
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Dialogs/run-1').flush(trace);
    await settle(fixture);

    const card = element.querySelector('.trace')!;
    expect(card.textContent).toContain('7 шагов');
    expect(card.textContent).toContain('искал задачи: молоко');
    expect(card.textContent).toContain('{"text":"молоко"}');
    expect(card.textContent).toContain('3 найдено');
  });

  it('should say the content is gone for an old run', async () => {
    const { fixture, element } = await render('run-old');

    httpMock.expectOne('/api/Admin/Dialogs/run-old').flush({ dialog: old, expired: true, steps: [], evaluation: null } satisfies AdminDialogTrace);
    await settle(fixture);

    expect(element.querySelector('.expired')?.textContent).toContain('Содержимого уже нет');
    expect(element.querySelectorAll('.step').length).toBe(0);
  });

  it('should show what the judge said above the steps', async () => {
    const { fixture, element } = await render();

    (element.querySelector('.dialog-row') as HTMLElement).click();
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Dialogs/run-1').flush({
      ...trace,
      evaluation: {
        modelName: 'Локальная 3090',
        succeeded: false,
        satisfaction: 2,
        clarifications: 1,
        replans: 0,
        agentMistake: true,
        stopReason: DialogStopReason.Repetition,
        notes: 'искал одно и то же три раза',
        createdUtc: '2026-09-29T08:00:00Z'
      }
    } satisfies AdminDialogTrace);
    await settle(fixture);

    const judge = element.querySelector('.trace .judge')!;
    expect(judge.textContent).toContain('Оценка судьи «Локальная 3090»');
    expect(judge.querySelector('.judge-facts')!.textContent!.trim()).toBe(
      'не дошёл до цели · оценка 2/5 · уточнений 1 · перепланов 0 · ошибка агента · упёрся: звал одно и то же по кругу');
    expect(judge.textContent).toContain('искал одно и то же три раза');
    // Судья — над шагами
    expect(judge.compareDocumentPosition(element.querySelector('.step')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('should show no judge block for a run not evaluated yet', async () => {
    const { fixture, element } = await render('run-1');

    httpMock.expectOne('/api/Admin/Dialogs/run-1').flush(trace);
    await settle(fixture);

    expect(element.querySelector('.trace')).not.toBeNull();
    expect(element.querySelector('.judge')).toBeNull();
  });

  function pick(element: HTMLElement, name: string, index: number): void {
    const select = element.querySelector<HTMLSelectElement>(`select[name="${name}"]`)!;
    select.selectedIndex = index;
    select.dispatchEvent(new Event('change'));
  }

  function type(element: HTMLElement, selector: string, value: string): void {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('should search by the picked signs only', async () => {
    const { fixture, element } = await render();

    pick(element, 'preset', 3);
    await settle(fixture);
    type(element, 'input[name="from"]', '2026-09-01');
    type(element, 'input[name="to"]', '2026-09-15');
    type(element, 'input[name="workspace"]', '  Дача ');
    pick(element, 'status', 4);
    pick(element, 'satisfaction', 1);
    await settle(fixture);

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    const params = httpMock.expectOne(r => r.url === '/api/Admin/Dialogs').request.params;

    expect(params.get('from')).toBe('2026-09-01');
    expect(params.get('to')).toBe('2026-09-15');
    expect(params.get('workspace')).toBe('Дача');
    expect(params.get('status')).toBe(String(AssistantRunStatus.Failed));
    expect(params.get('maxSatisfaction')).toBe('2');
    expect(params.has('modelId')).toBe(false);
    expect(params.has('minClarifications')).toBe(false);
  });

  it('should continue the shown list, not the edited filter, with the next page', async () => {
    const { fixture, element } = await render(null, 'run-old');

    // Фильтр поправили, но «Найти» не нажали — список на экране всё ещё прежний
    type(element, 'input[name="workspace"]', 'Дача');
    await settle(fixture);
    Array.from(element.querySelectorAll('button')).find(x => x.textContent!.trim() === 'Показать ещё')!.click();
    const req = httpMock.expectOne(r => r.url === '/api/Admin/Dialogs');

    expect(req.request.params.get('before')).toBe('run-old');
    expect(req.request.params.has('workspace')).toBe(false);
    req.flush({ dialogs: [{ ...dialog, runId: 'run-older' }], nextBefore: null });
    await settle(fixture);

    expect(element.querySelectorAll('.dialog-row').length).toBe(3);
    expect(Array.from(element.querySelectorAll('button')).some(x => x.textContent!.trim() === 'Показать ещё')).toBe(false);
  });

  it('should show the refusal when a trace does not open', async () => {
    const { fixture, element } = await render();

    (element.querySelector('.dialog-row') as HTMLElement).click();
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Dialogs/run-1').flush(null, { status: 404, statusText: 'Not Found' });
    await settle(fixture);
    await settle(fixture);

    expect(element.querySelector('.alert-danger')!.textContent).toContain('404 Not Found');
    expect(element.querySelector('.trace')).toBeNull();
  });
});
