import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AdminApiService } from '../core/admin-api.service';
import {
  AdminDialog,
  AdminDialogEvaluation,
  AdminDialogFilter,
  AdminDialogStep,
  AdminDialogTrace,
  AdminJob,
  AdminModel
} from '../core/admin.model';
import { apiErrorText } from '../core/api-error';
import { AdminJobStatus, JOB_POLL_MS, jobState } from '../core/admin-job';
import {
  AssistantRunStatus,
  AssistantStopReason,
  DIALOG_STOP_REASON_TITLES,
  RUN_STATUSES,
  RUN_STATUS_TITLES,
  STOP_REASON_TITLES
} from '../core/assistant-run-status';

/** Дни — `yyyy-MM-dd` по UTC, как их ждёт сервер */
function daysAgo(days: number): string {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() - days);
  return date.toISOString().slice(0, 10);
}

/** Стоимость одного хода бывает в сотых долях копейки — знаков больше, чем у запусков */
const runCost = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 4 });

const jobCost = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 });

/** Период списка: последние N дней, как на макете «за 7 дней», или свои даты */
type Preset = 7 | 30 | 90 | 'custom';

/**
 * Разбор диалогов (спец. admin, 16.2): список прогонов по признакам и трасса одного.
 * Поиска по тексту нет намеренно — только признаки.
 *
 * Открытый прогон живёт в адресе (`?run=`): ссылку на разбор можно передать. Каждое
 * открытие трассы сервер пишет в журнал доступа — об этом сказано прямо над списком.
 * Прогон старше срока трассы в списке остаётся, а открыть его значит получить ответ
 * «содержимого уже нет». Оценка судьи, если прогон оценён, — над шагами трассы.
 *
 * Автооценка (16.3) — только отсюда, руками: судья и размер пачки, по умолчанию своя карта.
 * Запуск идёт фоном, и список запусков перечитывается, пока он не кончится
 */
@Component({
  selector: 'adm-dialogs',
  imports: [FormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    pre { white-space: pre-wrap; word-break: break-word; max-height: 24rem; }
  `,
  template: `
    <form class="d-flex flex-wrap align-items-end gap-2 mb-2" (ngSubmit)="search()">
      <h2 class="h6 mb-0 me-auto">Диалоги</h2>
      <select class="form-select form-select-sm w-auto" name="preset" [(ngModel)]="preset">
        <option [ngValue]="7">за 7 дней</option>
        <option [ngValue]="30">за 30 дней</option>
        <option [ngValue]="90">за 90 дней</option>
        <option value="custom">свои даты</option>
      </select>
      @if (preset === 'custom') {
        <input class="form-control form-control-sm w-auto" type="date" name="from" [(ngModel)]="from">
        <input class="form-control form-control-sm w-auto" type="date" name="to" [(ngModel)]="to">
      }
      <input class="form-control form-control-sm w-auto" name="workspace" placeholder="пространство: название или id"
             [(ngModel)]="workspace">
      <select class="form-select form-select-sm w-auto" name="model" [(ngModel)]="modelId">
        <option [ngValue]="null">все модели</option>
        @for (model of models(); track model.id) {
          <option [ngValue]="model.id">{{ model.displayName }}</option>
        }
      </select>
      <select class="form-select form-select-sm w-auto" name="status" [(ngModel)]="status">
        <option [ngValue]="null">любой статус</option>
        @for (status of statuses; track status) {
          <option [ngValue]="status">{{ statusTitle(status) }}</option>
        }
      </select>
      <select class="form-select form-select-sm w-auto" name="clarifications" [(ngModel)]="minClarifications">
        <option [ngValue]="null">уточнения: любые</option>
        <option [ngValue]="1">от 1</option>
        <option [ngValue]="2">от 2</option>
        <option [ngValue]="3">от 3</option>
      </select>
      <select class="form-select form-select-sm w-auto" name="satisfaction" [(ngModel)]="maxSatisfaction">
        <option [ngValue]="null">оценка: любая</option>
        <option [ngValue]="2">не выше 2/5</option>
        <option [ngValue]="3">не выше 3/5</option>
        <option [ngValue]="4">не выше 4/5</option>
      </select>
      <button class="btn btn-sm btn-outline-primary" type="submit" [disabled]="busy()">Найти</button>
    </form>

    <div class="text-body-secondary small mb-2">
      Открытие диалога записывается в журнал доступа: кто, когда, чей прогон. Уточнения и оценку
      ставит автооценка — неоценённые прогоны под эти фильтры не попадают.
    </div>

    @if (error()) {
      <div class="alert alert-danger small py-2">{{ error() }}</div>
    }

    <div class="card card-body bg-white mb-3 evaluation">
      <form class="d-flex flex-wrap align-items-end gap-2 mb-1" (ngSubmit)="evaluate()">
        <span class="fw-semibold me-2">Автооценка</span>
        <div>
          <label class="form-label small mb-0" for="judge">Судья</label>
          <select class="form-select form-select-sm" id="judge" name="judge" [(ngModel)]="judgeId">
            @for (model of models(); track model.id) {
              <option [ngValue]="model.id">{{ judgeTitle(model) }}</option>
            }
          </select>
        </div>
        <div>
          <label class="form-label small mb-0" for="batchSize">Диалогов, до {{ maxBatch() }}</label>
          <input class="form-control form-control-sm" id="batchSize" name="batchSize" type="number" min="1"
                 [max]="maxBatch()" style="width: 100px" [(ngModel)]="batchSize">
        </div>
        <button class="btn btn-sm btn-outline-primary" type="submit"
                [disabled]="busy() || !judgeId || evaluationRunning()">Оценить</button>
      </form>
      <div class="small text-body-secondary mb-2">
        Неоценённые прогоны, новые первыми. Судья чужого поставщика получает только диалоги,
        прошедшие через него, — остальные пропускаются и видны в «пропущено»; своя карта судит любые.
        Оценка — наш расход, в лимит пространств не идёт. Один запуск за раз.
      </div>
      @if (evaluations().length) {
        <table class="table table-sm small mb-0">
          <thead>
            <tr>
              <th>Когда</th>
              <th>Судья</th>
              <th>Кто</th>
              <th>Состояние</th>
              <th class="text-end">оценено</th>
              <th class="text-end">пропущено</th>
              <th class="text-end">сорвалось</th>
              <th class="text-end">стоимость</th>
            </tr>
          </thead>
          <tbody>
            @for (job of evaluations(); track job.id) {
              <tr class="evaluation-row">
                <td class="text-nowrap">{{ job.createdUtc | date: 'dd.MM.yyyy HH:mm' }}</td>
                <td>{{ job.modelName }}</td>
                <td>{{ job.startedBy ?? '—' }}</td>
                <td class="job-state">
                  {{ jobState(job) }}
                  @if (job.error) {
                    <span class="text-danger">· {{ job.error }}</span>
                  }
                </td>
                <td class="text-end">{{ job.done }}</td>
                <td class="text-end">{{ job.skipped }}</td>
                <td class="text-end">{{ job.failed }}</td>
                <td class="text-end text-nowrap">{{ money(job.cost, jobCost) }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>

    <table class="table table-sm table-hover bg-white small mb-2">
      <thead>
        <tr>
          <th>Когда</th>
          <th>Пространство</th>
          <th>Модель</th>
          <th>Статус</th>
          <th class="text-end">Шагов</th>
          <th class="text-end">Уточнений</th>
          <th class="text-end">Оценка</th>
          <th class="text-end">Стоимость</th>
        </tr>
      </thead>
      <tbody>
        @for (dialog of dialogs(); track dialog.runId) {
          <tr class="dialog-row" role="button" [class.table-active]="trace()?.dialog?.runId === dialog.runId"
              (click)="open(dialog.runId)">
            <td class="text-nowrap">{{ dialog.startedUtc | date: 'dd.MM.yyyy HH:mm' }}</td>
            <td>
              «{{ dialog.workspaceName }}»
              @if (dialog.traceExpired) {
                <span class="text-body-secondary">· содержимого уже нет</span>
              }
            </td>
            <td>{{ dialog.modelName ?? '—' }}</td>
            <td>{{ statusTitle(dialog.status) }}</td>
            <td class="text-end">{{ dialog.steps }}</td>
            <td class="text-end">{{ dialog.clarifications ?? '—' }}</td>
            <td class="text-end">{{ dialog.satisfaction === null ? '—' : dialog.satisfaction + '/5' }}</td>
            <td class="text-end text-nowrap">{{ money(dialog.cost, runCost) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="8" class="text-body-secondary">Ничего не нашлось.</td></tr>
        }
      </tbody>
    </table>

    @if (nextBefore()) {
      <button class="btn btn-sm btn-outline-secondary mb-3" type="button" (click)="more()" [disabled]="busy()">Показать ещё</button>
    }

    @if (trace(); as trace) {
      <div class="card card-body bg-white mb-3 trace">
        <h3 class="h6">
          Прогон {{ trace.dialog.startedUtc | date: 'dd.MM.yyyy HH:mm' }} · «{{ trace.dialog.workspaceName }}» ·
          {{ trace.dialog.modelName ?? 'модель не записана' }} · {{ trace.dialog.steps }} шагов ·
          {{ statusTitle(trace.dialog.status) }}{{ stopReason(trace.dialog.stopReason) }}
        </h3>
        <div class="text-body-secondary small mb-2">
          прогон {{ trace.dialog.runId }} · токенов {{ trace.dialog.tokensIn }} / {{ trace.dialog.tokensOut }} ·
          стоимость {{ money(trace.dialog.cost, runCost) }}
        </div>

        @if (trace.evaluation; as evaluation) {
          <div class="border rounded p-2 mb-2 small judge">
            <div class="fw-semibold">
              Оценка судьи «{{ evaluation.modelName }}» · {{ evaluation.createdUtc | date: 'dd.MM.yyyy HH:mm' }}
            </div>
            <div class="judge-facts">{{ judgeFacts(evaluation) }}</div>
            @if (evaluation.notes) {
              <div class="text-body-secondary">{{ evaluation.notes }}</div>
            }
          </div>
        }

        @if (trace.expired) {
          <div class="alert alert-secondary small py-2 mb-0 expired">
            Содержимого уже нет: трасса старше срока хранения и вычищена. Прогон, модель и счётчики остаются.
          </div>
        } @else {
          @for (step of trace.steps; track $index) {
            <div class="border-top py-2 step">
              <div class="d-flex gap-3 small">
                <span class="text-body-secondary">{{ step.step }}</span>
                <span class="me-auto">{{ step.summary }}{{ step.toolName ? ' · ' + step.toolName : '' }}</span>
                <span class="text-body-secondary text-nowrap">{{ step.durationMs }} мс</span>
                <span class="text-body-secondary text-nowrap">{{ tokens(step) }}</span>
              </div>
              @if (step.error) {
                <div class="text-danger small">{{ step.error }}</div>
              }
              <details class="small">
                <summary>{{ step.kind === 'tool' ? 'аргументы и результат' : 'что ушло и что вернулось' }}</summary>
                <pre class="bg-body-tertiary p-2 mb-1">{{ step.input }}</pre>
                @if (step.output !== null) {
                  <pre class="bg-body-tertiary p-2 mb-0">{{ step.output }}</pre>
                }
              </details>
            </div>
          } @empty {
            <div class="text-body-secondary small">Шагов в трассе нет.</div>
          }
        }
      </div>
    }
  `
})
export class DialogsComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly statuses = RUN_STATUSES;
  protected readonly runCost = runCost;
  protected readonly jobCost = jobCost;

  protected preset: Preset = 7;
  protected from = daysAgo(6);
  protected to = daysAgo(0);
  protected workspace = '';
  protected modelId: string | null = null;
  protected status: AssistantRunStatus | null = null;
  protected minClarifications: number | null = null;
  protected maxSatisfaction: number | null = null;

  /**
   * Признаки, по которым построен показанный список: «Показать ещё» продолжает его,
   * а не форму — поправленный, но не отправленный фильтр смешал бы страницы двух выборок
   */
  private applied: AdminDialogFilter = this.filter();

  protected readonly models = signal<AdminModel[]>([]);
  /** Валюта прайса — из справочника моделей; не загрузился — суммы без подписи */
  protected readonly currency = signal('');
  protected readonly dialogs = signal<AdminDialog[]>([]);
  protected readonly nextBefore = signal<string | null>(null);
  protected readonly trace = signal<AdminDialogTrace | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Запуски автооценки, новые первыми, и потолок пачки из конфигурации */
  protected readonly evaluations = signal<AdminJob[]>([]);
  protected readonly maxBatch = signal(1);
  protected readonly evaluationRunning = computed(() =>
    this.evaluations().some(job => job.status === AdminJobStatus.Running));

  protected readonly jobState = jobState;

  /** Судья по умолчанию — своя карта: ей можно любой диалог (16.3) */
  protected judgeId: string | null = null;
  protected batchSize = 5;

  /** Перечитывание идущего запуска; снимается вместе с экраном */
  private poll: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopPolling());
  }

  async ngOnInit(): Promise<void> {
    const run = this.route.snapshot.queryParamMap.get('run');

    await this.run(async () => {
      this.applied = this.filter();
      // Модели нужны только фильтру, выбору судьи и подписи валюты: не загрузились —
      // список диалогов всё равно показываем, а отказ — плашкой
      const catalog = this.api.models().catch(error => {
        this.error.set(`Модели не загрузились: ${apiErrorText(error)}`);
        return null;
      });
      const [page, evaluations] = await Promise.all([
        this.api.dialogs(this.applied),
        this.api.evaluations()
      ]);
      const loaded = await catalog;
      const models = loaded?.models ?? [];
      this.models.set(models);
      this.currency.set(loaded?.currency ?? '');
      this.dialogs.set(page.dialogs);
      this.nextBefore.set(page.nextBefore);
      this.maxBatch.set(evaluations.maxBatchSize);
      this.batchSize = Math.min(this.batchSize, evaluations.maxBatchSize);
      this.evaluations.set(evaluations.jobs);
      this.judgeId = (models.find(model => model.isLocal) ?? models[0])?.id ?? null;
      this.schedule();
    });

    if (run) await this.load(run);
  }

  protected judgeTitle(model: AdminModel): string {
    return model.displayName + (model.isLocal ? ' · своя карта' : ` · ${model.provider}`) + (model.isEnabled ? '' : ' · выкл.');
  }

  protected money(value: number, format: Intl.NumberFormat): string {
    return `${format.format(value)} ${this.currency()}`.trim();
  }

  /** «удалось · 4/5 · уточнений 1 · перепланов 0 · ошибка агента · упёрся: …» — что судья сказал */
  protected judgeFacts(evaluation: AdminDialogEvaluation): string {
    const facts = [
      evaluation.succeeded === null ? null : evaluation.succeeded ? 'дошёл до цели' : 'не дошёл до цели',
      evaluation.satisfaction === null ? null : `оценка ${evaluation.satisfaction}/5`,
      evaluation.clarifications === null ? null : `уточнений ${evaluation.clarifications}`,
      evaluation.replans === null ? null : `перепланов ${evaluation.replans}`,
      evaluation.agentMistake ? 'ошибка агента' : null,
      evaluation.stopReason === null
        ? null
        : `упёрся: ${DIALOG_STOP_REASON_TITLES[evaluation.stopReason] ?? evaluation.stopReason}`
    ];
    return facts.filter(Boolean).join(' · ') || '—';
  }

  /** «Оценить»: запуск идёт фоном, экран перечитывает его до конца */
  protected async evaluate(): Promise<void> {
    const modelId = this.judgeId;
    if (!modelId) return;

    await this.run(async () => {
      const job = await this.api.startEvaluation({ modelId, batchSize: this.batchSize });
      this.evaluations.update(jobs => [job, ...jobs.filter(x => x.id !== job.id)]);
      this.schedule();
    });
  }

  protected statusTitle(status: AssistantRunStatus): string {
    return RUN_STATUS_TITLES[status] ?? `статус ${status}`;
  }

  protected stopReason(reason: AssistantStopReason | null): string {
    return reason === null ? '' : ` (${STOP_REASON_TITLES[reason] ?? reason})`;
  }

  protected tokens(step: AdminDialogStep): string {
    return step.tokensIn || step.tokensOut ? `${step.tokensIn} / ${step.tokensOut} ток.` : '';
  }

  protected async search(): Promise<void> {
    await this.run(async () => {
      this.applied = this.filter();
      const page = await this.api.dialogs(this.applied);
      this.dialogs.set(page.dialogs);
      this.nextBefore.set(page.nextBefore);
    });
  }

  protected async more(): Promise<void> {
    const before = this.nextBefore();
    if (!before) return;

    await this.run(async () => {
      const page = await this.api.dialogs(this.applied, before);
      this.dialogs.update(dialogs => [...dialogs, ...page.dialogs]);
      this.nextBefore.set(page.nextBefore);
    });
  }

  protected async open(runId: string): Promise<void> {
    await this.router.navigate([], { relativeTo: this.route, queryParams: { run: runId } });
    await this.load(runId);
  }

  private async load(runId: string): Promise<void> {
    await this.run(async () => this.trace.set(await this.api.dialog(runId)));
  }

  private filter(): AdminDialogFilter {
    const custom = this.preset === 'custom';
    return {
      from: custom ? this.from || null : daysAgo((this.preset as number) - 1),
      to: custom ? this.to || null : null,
      workspace: this.workspace.trim() || null,
      modelId: this.modelId,
      status: this.status,
      minClarifications: this.minClarifications,
      maxSatisfaction: this.maxSatisfaction
    };
  }

  /**
   * Пока запуск идёт, список запусков перечитывается; кончился — перечитывается и список
   * диалогов: у прогонов появились оценки
   */
  private schedule(): void {
    this.stopPolling();
    if (!this.evaluationRunning()) return;

    this.poll = setTimeout(async () => {
      this.poll = null;
      try {
        const evaluations = await this.api.evaluations();
        this.evaluations.set(evaluations.jobs);
        if (!this.evaluationRunning()) await this.search();
        this.schedule();
      } catch (error) {
        this.error.set(apiErrorText(error));
      }
    }, JOB_POLL_MS);
  }

  private stopPolling(): void {
    if (this.poll !== null) clearTimeout(this.poll);
    this.poll = null;
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (error) {
      this.error.set(apiErrorText(error));
    } finally {
      this.busy.set(false);
    }
  }
}
