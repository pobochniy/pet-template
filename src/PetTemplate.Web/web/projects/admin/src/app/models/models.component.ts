import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../core/admin-api.service';
import { AdminRole } from '../core/admin-role';
import { AdminSessionService } from '../core/admin-session.service';
import { AdminJob, AdminModel, AdminModelEdit, AdminModels } from '../core/admin.model';
import { apiErrorText } from '../core/api-error';
import { AdminJobStatus, JOB_POLL_MS, jobState } from '../core/admin-job';

/** Подписи долей сверки — ключи из `ModelCheckShareDto.Key` на сервере */
const SHARE_TITLES: Record<string, string> = {
  tool: 'инструмент угадан',
  russian: 'ответ по-русски',
  clean: 'без слов об устройстве',
  noCalls: 'без единого вызова',
  failed: 'сорвалось'
};

const amount = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 });

const price = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 6 });

/**
 * Справочник моделей (спец. admin, 15.1 и 15.2): цены по прайсу поставщика, включение
 * и порядок. Смотреть — любому админу, править — роли `Models`.
 *
 * Порядок уходит на сервер целиком: стрелка переставляет строку в списке, и на
 * сервер едет весь список в новом порядке, а не «подвинь эту вверх». Удаления нет —
 * только выключение. Цена ноль разрешена (своя карта, бесплатный тариф), отрицательная — нет.
 *
 * Правка несёт `version` строки, какой её видел экран: строку, которую за это время
 * поправили, сервер не перезапишет и откажет «откройте заново» — отказ показываем как есть
 * и перечитываем справочник. У модели, по которой уже ходили, поставщика и ключ не поменять —
 * другая модель заводится новой строкой.
 *
 * «Сверить» (15.3) гоняет набор фраз против строки — и выключенной — фоном: список сверок
 * перечитывается, пока какая-то идёт, а отчёт открывается по строке списка
 */
@Component({
  selector: 'adm-models',
  imports: [FormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="d-flex align-items-baseline gap-3 mb-2">
      <h2 class="h6 mb-0">Модели</h2>
      @if (canEdit()) {
        <button class="btn btn-sm btn-outline-primary ms-auto" type="button" (click)="add()" [disabled]="busy()">Добавить</button>
      }
    </div>

    @if (error()) {
      <div class="alert alert-danger small py-2">{{ error() }}</div>
    }

    @if (data(); as data) {
      @if (noneEnabled()) {
        <div class="alert alert-warning small py-2">Ни одной включённой модели — ассистент не отвечает.</div>
      }

      <table class="table table-sm align-middle bg-white">
        <thead>
          <tr>
            <th></th>
            <th></th>
            <th colspan="3" class="text-center fw-normal small text-body-secondary prices">{{ data.currency }} за 1000 токенов</th>
            <th colspan="3"></th>
          </tr>
          <tr>
            <th>#</th>
            <th>Модель</th>
            <th class="text-end">вход</th>
            <th class="text-end">кеш</th>
            <th class="text-end">выход</th>
            <th>вкл</th>
            <th>состояние</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          @for (model of data.models; track model.id; let first = $first, last = $last, i = $index) {
            <tr class="model-row">
              <td>{{ model.priority }}</td>
              <td>
                <div>{{ model.displayName }}</div>
                <div class="small text-body-secondary">
                  {{ model.provider }} / {{ model.key }}{{ model.isLocal ? ' · своя карта' : '' }}
                </div>
                <div class="small text-body-secondary">
                  правил {{ model.updatedBy ?? 'система' }}, {{ model.updatedUtc | date: 'dd.MM.yyyy HH:mm' }}
                </div>
              </td>
              <td class="text-end">{{ money(model.costIn) }}</td>
              <td class="text-end">{{ money(model.costCached) }}</td>
              <td class="text-end">{{ money(model.costOut) }}</td>
              <td>{{ model.isEnabled ? '☑' : '☐' }}</td>
              <td class="small state">{{ state(model) }}</td>
              <td class="text-nowrap text-end">
                @if (canEdit()) {
                  <button class="btn btn-sm btn-link" type="button" title="Выше" (click)="move(i, -1)" [disabled]="busy() || first">↑</button>
                  <button class="btn btn-sm btn-link" type="button" title="Ниже" (click)="move(i, 1)" [disabled]="busy() || last">↓</button>
                  <button class="btn btn-sm btn-link" type="button" (click)="edit(model)" [disabled]="busy()">Править</button>
                  @if (model.openUntilUtc) {
                    <button class="btn btn-sm btn-link" type="button" title="Отменить паузу размыкателя: модель ожила"
                            (click)="close(model)" [disabled]="busy()">Замкнуть</button>
                  }
                  <button class="btn btn-sm btn-link" type="button"
                          title="Прогнать набор фраз против модели — фоном, в изолированном мире"
                          (click)="check(model)" [disabled]="busy() || checkRunning()">Сверить</button>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
      @if (data.transcription; as voice) {
        <div class="small text-body-secondary mb-3 transcription">
          Расшифровка голоса: {{ voice.provider }}/{{ voice.model }}, {{ money(voice.costPerMinute) }} {{ data.currency }} за минуту —
          из конфигурации, не строка справочника
        </div>
      }
    }

    @if (form(); as form) {
      <form class="card card-body bg-white mb-3" style="max-width: 640px" (ngSubmit)="save()">
        <h3 class="h6">{{ editingId() ? 'Правка модели' : 'Новая модель' }}</h3>
        <div class="row g-2 mb-2">
          <div class="col-4">
            <label class="form-label small mb-0" for="provider">Поставщик</label>
            <input class="form-control form-control-sm" id="provider" name="provider" [(ngModel)]="form.provider"
                   [disabled]="locked()" required>
          </div>
          <div class="col-8">
            <label class="form-label small mb-0" for="key">Ключ модели у поставщика</label>
            <input class="form-control form-control-sm" id="key" name="key" [(ngModel)]="form.key" [disabled]="locked()" required>
          </div>
          @if (locked()) {
            <div class="col-12 small text-body-secondary used">
              По этой модели уже ходили — поставщика и ключ не поменять: другая модель — заведите новую модель
            </div>
          }
          <div class="col-12">
            <label class="form-label small mb-0" for="displayName">Название</label>
            <input class="form-control form-control-sm" id="displayName" name="displayName" [(ngModel)]="form.displayName" required>
          </div>
        </div>
        <div class="small text-body-secondary mb-1">
          Цена по прайсу поставщика — {{ data()?.currency }} за 1000 токенов; ноль можно, отрицательную нельзя.
          У своей карты — оценка себестоимости
        </div>
        <div class="row g-2 mb-2">
          <div class="col-4">
            <label class="form-label small mb-0" for="costIn">вход</label>
            <input class="form-control form-control-sm" id="costIn" name="costIn" type="number" min="0" step="any" [(ngModel)]="form.costIn" required>
          </div>
          <div class="col-4">
            <label class="form-label small mb-0" for="costCached">кеш</label>
            <input class="form-control form-control-sm" id="costCached" name="costCached" type="number" min="0" step="any" [(ngModel)]="form.costCached" required>
          </div>
          <div class="col-4">
            <label class="form-label small mb-0" for="costOut">выход</label>
            <input class="form-control form-control-sm" id="costOut" name="costOut" type="number" min="0" step="any" [(ngModel)]="form.costOut" required>
          </div>
        </div>
        <div class="form-check">
          <input class="form-check-input" id="isLocal" name="isLocal" type="checkbox" [(ngModel)]="form.isLocal">
          <label class="form-check-label small" for="isLocal">
            Своя видеокарта — переписка никуда не уходит, судья автооценки на ней разбирает любой диалог
          </label>
        </div>
        <div class="form-check mb-2">
          <input class="form-check-input" id="isEnabled" name="isEnabled" type="checkbox" [(ngModel)]="form.isEnabled">
          <label class="form-check-label small" for="isEnabled">Включена</label>
        </div>
        @if (leavesNoneEnabled()) {
          <div class="alert alert-warning small py-2 last-enabled">
            Это последняя включённая модель: после сохранения ассистент перестанет отвечать.
          </div>
        }
        <div class="d-flex gap-2">
          <button class="btn btn-sm btn-primary" type="submit" [disabled]="busy()">Сохранить</button>
          <button class="btn btn-sm btn-outline-secondary" type="button" (click)="cancel()" [disabled]="busy()">Отмена</button>
        </div>
      </form>
    }

    <h3 class="h6 mt-3">Сверки</h3>
    <div class="small text-body-secondary mb-2">
      Набор фраз против модели — настоящим ходом, но в изолированном мире: в базе остаются
      только запуск и его расходы. Одна сверка за раз.
    </div>
    <table class="table table-sm table-hover bg-white small mb-3">
      <thead>
        <tr>
          <th>Когда</th>
          <th>Модель</th>
          <th>Кто</th>
          <th>Состояние</th>
          <th class="text-end">токенов</th>
          <th class="text-end">{{ data()?.currency }}</th>
        </tr>
      </thead>
      <tbody>
        @for (job of checks(); track job.id) {
          <tr class="check-row" role="button" [class.table-active]="report()?.id === job.id" (click)="openReport(job.id)">
            <td class="text-nowrap">{{ job.createdUtc | date: 'dd.MM.yyyy HH:mm' }}</td>
            <td>{{ job.modelName }}</td>
            <td>{{ job.startedBy ?? '—' }}</td>
            <td class="check-state">
              {{ checkState(job) }}
              @if (job.error) {
                <span class="text-danger">· {{ job.error }}</span>
              }
            </td>
            <td class="text-end text-nowrap">{{ job.tokensIn + job.tokensOut }}</td>
            <td class="text-end text-nowrap">{{ cost(job.cost) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="6" class="text-body-secondary">Сверок ещё не было.</td></tr>
        }
      </tbody>
    </table>

    @if (report(); as job) {
      <div class="card card-body bg-white mb-3 check-report">
        <div class="d-flex align-items-baseline gap-2 mb-2">
          <h3 class="h6 mb-0 me-auto">Сверка «{{ job.modelName }}» · {{ checkState(job) }}</h3>
          <button class="btn btn-sm btn-link p-0" type="button" (click)="openReport(job.id)" [disabled]="busy()">обновить</button>
        </div>
        @if (job.report; as report) {
          <table class="table table-sm small mb-2 shares" style="max-width: 420px">
            <tbody>
              @for (share of report.shares; track share.key) {
                <tr>
                  <td>{{ shareTitle(share.key) }}</td>
                  <td class="text-end text-nowrap">
                    {{ share.considered ? share.hits + '/' + share.considered + ' (' + percent(share.hits, share.considered) + ' %)' : '—' }}
                  </td>
                </tr>
              }
              <tr>
                <td>шагов в среднем</td>
                <td class="text-end">{{ steps(report.averageSteps) }}</td>
              </tr>
            </tbody>
          </table>
          <table class="table table-sm small mb-0 phrases">
            <thead>
              <tr>
                <th></th>
                <th>Фраза</th>
                <th>Ждали</th>
                <th>Позвано</th>
                <th>Ответ</th>
                <th class="text-end">{{ data()?.currency }}</th>
              </tr>
            </thead>
            <tbody>
              @for (result of report.results; track $index) {
                <tr class="phrase-row">
                  <td>{{ result.toolHit ? '✓' : '✗' }}</td>
                  <td>{{ result.phrase }}</td>
                  <td>{{ result.expectedTool ?? '—' }}</td>
                  <td>{{ result.tools.join(', ') || '—' }}</td>
                  <td>
                    @if (result.error) {
                      <span class="text-danger">{{ result.error }}</span>
                    } @else {
                      {{ result.answer }}
                      @if (marks(result.cleanAnswer, result.russianAnswer); as marks) {
                        <span class="text-warning-emphasis">· {{ marks }}</span>
                      }
                    }
                  </td>
                  <td class="text-end text-nowrap">{{ cost(result.cost) }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    }
  `
})
export class ModelsComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly session = inject(AdminSessionService);

  /** Перечитывание идущей сверки; снимается вместе с экраном */
  private poll: ReturnType<typeof setTimeout> | null = null;

  protected readonly data = signal<AdminModels | null>(null);

  protected readonly checks = signal<AdminJob[]>([]);

  /** Открытая сверка с отчётом */
  protected readonly report = signal<AdminJob | null>(null);

  /** Одна сверка за раз (15.3): пока идёт, «Сверить» погашено — сервер всё равно откажет */
  protected readonly checkRunning = computed(() => this.checks().some(job => job.status === AdminJobStatus.Running));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopPolling());
  }
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Форма правки; `editingId` пуст у новой модели */
  protected readonly form = signal<AdminModelEdit | null>(null);
  protected readonly editingId = signal<string | null>(null);

  /** По модели уже ходили — поставщик и ключ в форме не правятся (15.1) */
  protected readonly locked = signal(false);

  protected readonly canEdit = computed(() => this.session.hasRole(AdminRole.Models));

  protected readonly noneEnabled = computed(() => !this.data()?.models.some(model => model.isEnabled));

  async ngOnInit(): Promise<void> {
    await Promise.all([this.load(), this.loadChecks()]);
  }

  protected checkState(job: AdminJob): string {
    return jobState(job);
  }

  protected cost(value: number): string {
    return amount.format(value);
  }

  protected shareTitle(key: string): string {
    return SHARE_TITLES[key] ?? key;
  }

  protected percent(hits: number, considered: number): number {
    return Math.round((100 * hits) / considered);
  }

  protected steps(value: number): string {
    return value.toLocaleString('ru-RU', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  }

  /** Промахи ответа по признакам сверки; `null` у «без слов об устройстве» — не задано у фразы */
  protected marks(clean: boolean | null, russian: boolean): string {
    return [clean === false ? 'слова об устройстве' : null, russian ? null : 'не по-русски']
      .filter(Boolean)
      .join(', ');
  }

  /** Сверить — в том числе выключенную: кандидата проверяют до включения (15.3) */
  protected async check(model: AdminModel): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      const job = await this.api.checkModel(model.id);
      this.checks.update(jobs => [job, ...jobs.filter(x => x.id !== job.id)]);
      this.report.set(job);
      this.schedule();
    } catch (error) {
      this.error.set(apiErrorText(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** Отчёт на сейчас: у идущей сверки — сделанные фразы, у законченной — все */
  protected async openReport(jobId: string): Promise<void> {
    try {
      this.report.set(await this.api.modelCheck(jobId));
      this.schedule();
    } catch (error) {
      this.error.set(apiErrorText(error));
    }
  }

  protected money(value: number): string {
    return price.format(value);
  }

  /**
   * Колонка «состояние» (спец. assistant, 16.2): выключена, разомкнута размыкателем
   * до срока, пауза вышла и ждёт пробного хода — или работает, с отказами подряд, если они есть
   */
  protected state(model: AdminModel): string {
    if (!model.isEnabled) return '○ выкл.';

    if (model.openUntilUtc) {
      const open = new Date(model.openUntilUtc);
      return open.getTime() > Date.now()
        ? `◐ разомкнута до ${open.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}`
        : '◐ пауза вышла, ждёт пробного хода';
    }

    return model.failureCount > 0 ? `● работает, отказов подряд: ${model.failureCount}` : '● работает';
  }

  /**
   * Выключить последнюю включённую — можно (15.1), но об этом предупреждаем: ассистенту
   * станет некуда ходить
   */
  protected leavesNoneEnabled(): boolean {
    const form = this.form();
    if (!form || form.isEnabled) return false;

    const others = this.data()?.models.filter(model => model.isEnabled && model.id !== this.editingId()) ?? [];
    return others.length === 0;
  }

  protected add(): void {
    this.editingId.set(null);
    this.locked.set(false);
    this.form.set({
      provider: '',
      key: '',
      displayName: '',
      costIn: 0,
      costCached: 0,
      costOut: 0,
      isEnabled: true,
      isLocal: false,
      version: 0
    });
  }

  protected edit(model: AdminModel): void {
    this.editingId.set(model.id);
    this.locked.set(model.isUsed);
    this.form.set({
      provider: model.provider,
      key: model.key,
      displayName: model.displayName,
      costIn: model.costIn,
      costCached: model.costCached,
      costOut: model.costOut,
      isEnabled: model.isEnabled,
      isLocal: model.isLocal,
      version: model.version
    });
  }

  protected cancel(): void {
    this.form.set(null);
    this.editingId.set(null);
    this.locked.set(false);
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (!form) return;

    // Ноль — можно (своя карта, бесплатный тариф); отрицательная цена — опечатка
    if ([form.costIn, form.costCached, form.costOut].some(cost => cost === null || !(cost >= 0))) {
      this.error.set('Цена не может быть отрицательной');
      return;
    }

    const id = this.editingId();
    const saved = await this.run(() => (id ? this.api.updateModel(id, { ...form }) : this.api.createModel({ ...form })));
    if (saved) this.cancel();
  }

  /** Отмена паузы размыкателя — не выбор модели и не смена порядка (спец. assistant, 16.2) */
  protected async close(model: AdminModel): Promise<void> {
    await this.run(() => this.api.closeModel(model.id));
  }

  protected async move(index: number, delta: number): Promise<void> {
    const ids = this.data()!.models.map(model => model.id);
    [ids[index], ids[index + delta]] = [ids[index + delta], ids[index]];

    await this.run(() => this.api.reorderModels(ids));
  }

  /**
   * Действие и перечитывание справочника после него; `false` — сервер отказал. Справочник
   * перечитывается и после отказа: чаще всего он значит, что экран открыт до чужой правки
   * (версия строки, порядок без новой модели), и показывать старое незачем
   */
  private async run(action: () => Promise<unknown>): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
      await this.load();
      return true;
    } catch (error) {
      this.error.set(apiErrorText(error));
      await this.load(false);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  /** `report` — показать отказ чтения; после отказа действия он затёр бы причину */
  private async load(report = true): Promise<void> {
    try {
      this.data.set(await this.api.models());
    } catch (error) {
      if (report) this.error.set(apiErrorText(error));
    }
  }

  private async loadChecks(): Promise<void> {
    try {
      this.checks.set(await this.api.modelChecks());
      this.schedule();
    } catch (error) {
      this.error.set(apiErrorText(error));
    }
  }

  /**
   * Пока сверка идёт, экран перечитывает список и открытый отчёт: она фоном, минутами,
   * и отчёт растёт по фразе
   */
  private schedule(): void {
    this.stopPolling();
    const running = this.checkRunning() || this.report()?.status === AdminJobStatus.Running;
    if (!running) return;

    this.poll = setTimeout(async () => {
      this.poll = null;
      const open = this.report();
      await this.loadChecks();
      if (open) await this.openReport(open.id);
    }, JOB_POLL_MS);
  }

  private stopPolling(): void {
    if (this.poll !== null) clearTimeout(this.poll);
    this.poll = null;
  }
}
