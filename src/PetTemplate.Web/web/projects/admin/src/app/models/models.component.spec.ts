import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ModelsComponent } from './models.component';
import { AdminSessionService } from '../core/admin-session.service';
import { AdminRole } from '../core/admin-role';
import { AdminJob, AdminModel, AdminModels } from '../core/admin.model';
import { AdminJobKind, AdminJobStatus } from '../core/admin-job';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

/** Справочник моделей (спец. admin, 15): смотреть любому админу, править — роли «Модели» */
describe('ModelsComponent', () => {
  let httpMock: HttpTestingController;

  function model(id: string, priority: number, patch: Partial<AdminModel> = {}): AdminModel {
    return {
      id,
      priority,
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
      version: 7,
      ...patch
    };
  }

  function catalog(models: AdminModel[], patch: Partial<AdminModels> = {}): AdminModels {
    return { currency: 'RUB', models, transcription: null, ...patch };
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [ModelsComponent],
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function check(patch: Partial<AdminJob> = {}): AdminJob {
    return {
      id: 'job-1',
      kind: AdminJobKind.ModelCheck,
      modelId: 'flash',
      modelName: 'DeepSeek-V4.1-Flash',
      startedBy: 'Oleg',
      createdUtc: '2026-10-01T10:00:00Z',
      finishedUtc: null,
      status: AdminJobStatus.Running,
      planned: 30,
      done: 4,
      skipped: 0,
      failed: 0,
      tokensIn: 12_000,
      tokensCached: 0,
      tokensOut: 2000,
      cost: 4.6,
      error: null,
      report: null,
      ...patch
    };
  }

  async function render(roles: AdminRole[], models: AdminModel[] | AdminModels, checks: AdminJob[] = []) {
    TestBed.inject(AdminSessionService).start({
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      admin: { userId: 'u-1', userName: 'Oleg', roles }
    });

    const fixture = TestBed.createComponent(ModelsComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/Admin/Models').flush(Array.isArray(models) ? catalog(models) : models);
    httpMock.expectOne('/api/Admin/Models/Checks').flush(checks);
    await settle(fixture);

    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function button(element: HTMLElement, text: string): HTMLButtonElement | undefined {
    return Array.from(element.querySelectorAll('button')).find(x => x.textContent!.trim() === text);
  }

  it('should show prices in the price list currency and the voice model from the config', async () => {
    const { element } = await render([AdminRole.Billing], catalog([model('flash', 1)], {
      currency: 'USD',
      transcription: { provider: 'openai', model: 'whisper-1', costPerMinute: 0.006 }
    }));

    expect(element.querySelector('.prices')!.textContent!.trim()).toBe('USD за 1000 токенов');
    expect(element.querySelector('.transcription')!.textContent!.replace(/\s+/g, ' ').trim()).toBe(
      'Расшифровка голоса: openai/whisper-1, 0,006 USD за минуту — из конфигурации, не строка справочника');
  });

  it('should not mention the voice model when voice is off', async () => {
    const { element } = await render([AdminRole.Billing], [model('flash', 1)]);

    expect(element.querySelector('.transcription')).toBeNull();
  });

  it('should show the list read-only without the models role', async () => {
    const { element } = await render([AdminRole.Billing], [model('flash', 1)]);

    expect(element.querySelectorAll('.model-row').length).toBe(1);
    expect(button(element, 'Добавить')).toBeUndefined();
    expect(button(element, 'Править')).toBeUndefined();
  });

  it('should send the whole list in the new order', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('flash', 1), model('local', 2)]);

    element.querySelectorAll<HTMLButtonElement>('button[title="Выше"]')[1].click();
    const req = httpMock.expectOne('/api/Admin/Models/Order');

    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(['local', 'flash']);
    req.flush(null);
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Models').flush(catalog([model('local', 1), model('flash', 2)]));
    await settle(fixture);

    expect(element.querySelectorAll('.model-row')[0].textContent).toContain('local');
  });

  it('should show until when the breaker keeps a model open', async () => {
    const until = new Date(Date.now() + 60 * 1000);
    const { element } = await render([AdminRole.Billing], [
      model('local', 1, { openUntilUtc: until.toISOString(), failureCount: 3 }),
      model('flash', 2, { failureCount: 2 })
    ]);

    const states = element.querySelectorAll('.state');
    const time = until.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
    expect(states[0].textContent!.trim()).toBe(`◐ разомкнута до ${time}`);
    expect(states[1].textContent!.trim()).toBe('● работает, отказов подряд: 2');
  });

  it('should say an open model waits for a probe once the pause is over', async () => {
    const { element } = await render([AdminRole.Billing], [
      model('local', 1, { openUntilUtc: new Date(Date.now() - 1000).toISOString(), failureCount: 3 })
    ]);

    expect(element.querySelector('.state')!.textContent!.trim()).toBe('◐ пауза вышла, ждёт пробного хода');
  });

  it('should close an open model by hand', async () => {
    const open = model('local', 1, { openUntilUtc: new Date(Date.now() + 60 * 1000).toISOString(), failureCount: 3 });
    const { fixture, element } = await render([AdminRole.Models], [open, model('flash', 2)]);

    expect(element.querySelectorAll('.model-row')[1].textContent).not.toContain('Замкнуть');
    button(element, 'Замкнуть')!.click();
    const req = httpMock.expectOne('/api/Admin/Models/local/Close');

    expect(req.request.method).toBe('POST');
    req.flush(null);
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Models').flush(catalog([model('local', 1), model('flash', 2)]));
    await settle(fixture);
    expect(button(element, 'Замкнуть')).toBeUndefined();
  });

  it('should not offer closing without the models role', async () => {
    const { element } = await render([AdminRole.Billing], [
      model('local', 1, { openUntilUtc: new Date(Date.now() + 60 * 1000).toISOString(), failureCount: 3 })
    ]);

    expect(button(element, 'Замкнуть')).toBeUndefined();
  });

  it('should warn before turning the last enabled model off', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('flash', 1), model('yandex', 2, { isEnabled: false })]);

    button(element, 'Править')!.click();
    await settle(fixture);
    element.querySelector<HTMLInputElement>('#isEnabled')!.click();
    await settle(fixture);

    expect(element.querySelector('.last-enabled')).not.toBeNull();
  });

  it('should not warn while another model stays enabled', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('flash', 1), model('local', 2)]);

    button(element, 'Править')!.click();
    await settle(fixture);
    element.querySelector<HTMLInputElement>('#isEnabled')!.click();
    await settle(fixture);

    expect(element.querySelector('.last-enabled')).toBeNull();
  });

  async function type(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }, element: HTMLElement, id: string, value: string) {
    const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  it('should add a free own-card model', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('flash', 1)]);

    button(element, 'Добавить')!.click();
    await settle(fixture);
    await type(fixture, element, 'provider', 'local');
    await type(fixture, element, 'key', 'qwen3');
    await type(fixture, element, 'displayName', 'Локальная 3090');
    // Своя видеокарта — судья автооценки на ней разбирает любой диалог (спец. admin, 16.3)
    element.querySelector<HTMLInputElement>('#isLocal')!.click();
    await settle(fixture);

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    const req = httpMock.expectOne('/api/Admin/Models');
    expect(req.request.method).toBe('POST');
    // Нулевая цена — законна (15.1)
    expect(req.request.body).toEqual({
      provider: 'local',
      key: 'qwen3',
      displayName: 'Локальная 3090',
      costIn: 0,
      costCached: 0,
      costOut: 0,
      isEnabled: true,
      isLocal: true,
      version: 0
    });
    req.flush('m-new');
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Models').flush(catalog([model('flash', 1), model('m-new', 2, { isLocal: true })]));
    await settle(fixture);
    // Форма закрывается уже после перечитывания — ещё круг
    await settle(fixture);

    expect(element.querySelector('form')).toBeNull();
    expect(element.querySelectorAll('.model-row').length).toBe(2);
    expect(element.querySelectorAll('.model-row')[1].textContent).toContain('своя карта');
  });

  it('should save an edited model by its id with the version it was read at', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('flash', 1)]);

    button(element, 'Править')!.click();
    await settle(fixture);
    await type(fixture, element, 'costOut', '0.6');

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    const req = httpMock.expectOne('/api/Admin/Models/flash');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(jasmine.objectContaining({ key: 'flash', costIn: 0.3, costOut: 0.6, version: 7 }));
    req.flush(null);
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Models').flush(catalog([model('flash', 1, { costOut: 0.6, version: 8 })]));
    await settle(fixture);
    await settle(fixture);

    expect(element.querySelector('form')).toBeNull();
  });

  it('should lock the provider and the key of a model already used', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('flash', 1, { isUsed: true })]);

    button(element, 'Править')!.click();
    await settle(fixture);
    await settle(fixture);

    expect(element.querySelector<HTMLInputElement>('#provider')!.disabled).toBeTrue();
    expect(element.querySelector<HTMLInputElement>('#key')!.disabled).toBeTrue();
    expect(element.querySelector<HTMLInputElement>('#displayName')!.disabled).toBeFalse();
    expect(element.querySelector('.used')!.textContent).toContain('заведите новую модель');
  });

  it('should show the stale-version refusal as is and re-read the list', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('flash', 1)]);

    button(element, 'Править')!.click();
    await settle(fixture);
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    httpMock.expectOne('/api/Admin/Models/flash').flush(
      { error: 'Модель уже поменялась — откройте справочник заново и повторите правку' },
      { status: 400, statusText: 'Bad Request' });
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Models').flush(catalog([model('flash', 1, { displayName: 'Чужая правка', version: 8 })]));
    await settle(fixture);
    await settle(fixture);

    expect(element.querySelector('.alert-danger')!.textContent).toContain('откройте справочник заново');
    expect(element.querySelector('.model-row')!.textContent).toContain('Чужая правка');
    expect(element.querySelector('form')).not.toBeNull();
    expect(button(element, 'Сохранить')!.disabled).toBeFalse();
  });

  it('should refuse a negative price before asking the server', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('flash', 1)]);

    button(element, 'Править')!.click();
    await settle(fixture);
    await type(fixture, element, 'costCached', '-1');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);

    httpMock.expectNone('/api/Admin/Models/flash');
    expect(element.querySelector('.alert-danger')!.textContent).toContain('отрицательной');
  });

  it('should warn when no model is enabled', async () => {
    const { element } = await render([AdminRole.Billing], [model('flash', 1, { isEnabled: false })]);

    expect(element.textContent).toContain('Ни одной включённой модели');
    expect(element.querySelector('.state')!.textContent!.trim()).toBe('○ выкл.');
  });

  it('should check a disabled model too and show the running check', async () => {
    const { fixture, element } = await render([AdminRole.Models], [model('yandex', 1, { isEnabled: false })]);

    button(element, 'Сверить')!.click();
    const req = httpMock.expectOne('/api/Admin/Models/yandex/Check');
    expect(req.request.method).toBe('POST');
    req.flush(check({ modelId: 'yandex', modelName: 'yandex', done: 0 }));
    await settle(fixture);

    expect(element.querySelector('.check-row .check-state')!.textContent).toContain('идёт · 0 из 30');
    expect(button(element, 'Сверить')!.disabled).toBeTrue();
  });

  it('should not offer a check without the models role', async () => {
    const { element } = await render([AdminRole.Billing], [model('flash', 1)]);

    expect(button(element, 'Сверить')).toBeUndefined();
  });

  it('should open the report of a finished check', async () => {
    const done = check({ status: AdminJobStatus.Done, done: 30, finishedUtc: '2026-10-01T10:20:00Z' });
    const { fixture, element } = await render([AdminRole.Billing], [model('flash', 1)], [done]);

    (element.querySelector('.check-row') as HTMLElement).click();
    httpMock.expectOne('/api/Admin/Models/Checks/job-1').flush({
      ...done,
      report: {
        phrases: 30,
        averageSteps: 1.5,
        shares: [
          { key: 'tool', hits: 27, considered: 30 },
          { key: 'clean', hits: 0, considered: 0 }
        ],
        results: [
          {
            phrase: 'заведи задачу починить кран',
            expectedTool: 'create_issue',
            tools: ['create_issue'],
            answer: 'Записал в план.',
            error: null,
            steps: 2,
            tokensIn: 400,
            tokensOut: 70,
            cost: 0.15,
            toolHit: true,
            cleanAnswer: false,
            russianAnswer: true
          }
        ]
      }
    } satisfies AdminJob);
    await settle(fixture);

    expect(element.querySelector('.check-report h3')!.textContent).toContain('готово');
    const shares = Array.from(element.querySelectorAll('.shares tr')).map(x =>
      Array.from(x.querySelectorAll('td')).map(td => td.textContent!.trim()));
    expect(shares).toEqual([
      ['инструмент угадан', '27/30 (90 %)'],
      ['без слов об устройстве', '—'],
      ['шагов в среднем', '1,5']
    ]);
    const phrase = element.querySelector('.phrase-row')!.textContent!;
    expect(phrase).toContain('заведи задачу починить кран');
    expect(phrase).toContain('слова об устройстве');
    expect(phrase).toContain('0,15');
  });
});
