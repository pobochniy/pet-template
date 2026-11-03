import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../core/admin-api.service';
import { AdminRole } from '../core/admin-role';
import { AdminSessionService } from '../core/admin-session.service';
import {
  AdminAssistantLimit,
  AdminBillingProvider,
  AdminBillingSummary,
  AdminBillingWorkspaceCard,
  AdminLedgerEntry,
  AdminLedgerFact
} from '../core/admin.model';
import { apiErrorText } from '../core/api-error';
import { ProviderLedgerKind } from '../core/provider-ledger-kind';
import { SERVICE_SPEND_TITLES, ServiceSpendKind } from '../core/service-spend-kind';

const money = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 0, maximumFractionDigits: 2 });
const whole = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const millions = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 });

/** Выбор периода: «за 30 дней», плюс календарные месяцы — под факт из кабинета поставщика */
type Preset = 'last30' | 'month' | 'prevMonth' | 'custom';

/** Дни — `yyyy-MM-dd` по UTC, как их ждёт сервер */
function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function addDays(day: string, days: number): string {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function period(preset: Exclude<Preset, 'custom'>): { from: string; to: string } {
  const now = today();
  const monthStart = `${now.slice(0, 7)}-01`;

  switch (preset) {
    case 'last30':
      return { from: addDays(now, -29), to: now };
    case 'month':
      return { from: monthStart, to: now };
    case 'prevMonth': {
      const to = addDays(monthStart, -1);
      return { from: `${to.slice(0, 7)}-01`, to };
    }
  }
}

/** Пустое поле формы лимита — «без потолка», а не ноль */
function limitOrNull(value: number | string | null | undefined): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}

/**
 * «Расход» (спец. admin, 14; assistant, 13.5–13.6): во что обходится ассистент, сходится ли
 * расчёт с фактом поставщика, наш счёт у поставщика и лимиты пространств. Смотреть — любому
 * админу; записывать платежи, факт и лимит — роли «Расход» (`Billing`).
 *
 * Баланса нет: деньги только учитываются, в валюте прайса справочника моделей. Служебные
 * расходы (компакция) стоят отдельной строкой: в лимит пространства они не идут, но поставщику
 * ушли. Факт сверяется с расчётом за свои дни, а не за выбранный период
 */
@Component({
  selector: 'adm-billing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe],
  template: `
    <div class="d-flex flex-wrap align-items-end gap-2 mb-3">
      <h2 class="h6 mb-0 me-auto">Расход</h2>
      <select class="form-select form-select-sm w-auto" name="preset" [ngModel]="preset()" (ngModelChange)="pick($event)">
        <option value="last30">за 30 дней</option>
        <option value="month">этот месяц</option>
        <option value="prevMonth">прошлый месяц</option>
        <option value="custom">свои даты</option>
      </select>
      @if (preset() === 'custom') {
        <input class="form-control form-control-sm w-auto" type="date" name="from" [(ngModel)]="from" (change)="load()">
        <input class="form-control form-control-sm w-auto" type="date" name="to" [(ngModel)]="to" (change)="load()">
      }
      <select class="form-select form-select-sm w-auto" name="model" [(ngModel)]="modelId" (ngModelChange)="load()">
        <option [ngValue]="null">все модели</option>
        @for (model of data()?.models ?? []; track model.modelId) {
          <option [ngValue]="model.modelId">{{ model.displayName }}</option>
        }
      </select>
    </div>

    @if (error()) {
      <div class="alert alert-danger small py-2" id="billing-error">{{ error() }}</div>
    }

    @if (data(); as data) {
      <table class="table table-sm bg-white mb-3 totals" style="max-width: 760px">
        <tbody>
          <tr>
            <td>Ходы ассистента</td>
            <td class="text-end text-nowrap">{{ whole(data.runs) }} · {{ tokens(data.tokens) }} ток.</td>
            <td class="text-end text-nowrap" id="runs-cost">{{ money(data.cost) }}</td>
          </tr>
          <tr class="service">
            <td>
              Служебные расходы (в лимит пространств не идут)
              <div class="small text-body-secondary service-kinds">
                @for (spend of data.service; track spend.kind; let last = $last) {
                  {{ serviceTitle(spend.kind) }} {{ money(spend.cost) }}{{ last ? '' : ' · ' }}
                }
              </div>
            </td>
            <td></td>
            <td class="text-end text-nowrap" id="service-cost">{{ money(data.serviceCost) }}</td>
          </tr>
          <tr class="fw-semibold">
            <td>Всего по прайсу поставщиков</td>
            <td></td>
            <td class="text-end text-nowrap" id="total-cost">{{ money(data.totalCost) }}</td>
          </tr>
          <tr class="small text-body-secondary">
            <td colspan="3" id="default-limit">Лимит пространства по умолчанию: {{ limitText(data.defaultLimit) }}</td>
          </tr>
        </tbody>
      </table>

      @for (provider of data.providers; track provider.provider) {
        <div class="card card-body bg-white mb-3 provider" style="max-width: 760px">
          <h3 class="h6">Поставщик {{ provider.provider }}</h3>

          <table class="table table-sm mb-2">
            <tbody>
              <tr>
                <td>Факт у поставщика за период</td>
                <td class="text-end text-nowrap fact">{{ provider.fact === null ? 'не введён' : money(provider.fact) }}</td>
                <td class="text-end text-nowrap">
                  @if (canWrite()) {
                    <button class="btn btn-sm btn-link p-0" type="button" (click)="openFact(provider)" [disabled]="busy()">ввести факт</button>
                  }
                </td>
              </tr>
              @for (fact of provider.facts; track fact.id) {
                <tr class="small text-body-secondary">
                  <td class="ps-3">
                    за {{ day(fact.periodFrom) }}–{{ day(fact.periodTo) }}
                    · ввёл {{ fact.actorName ?? '—' }}, {{ fact.createdUtc | date: 'dd.MM.yyyy HH:mm' }}
                    {{ fact.comment ? '· «' + fact.comment + '»' : '' }}
                  </td>
                  <td class="text-end text-nowrap">{{ money(fact.amount) }}</td>
                  <td class="text-end">
                    @if (canWrite()) {
                      <button class="btn btn-sm btn-link p-0" type="button" (click)="openFact(provider, fact)" [disabled]="busy()">изменить</button>
                    }
                  </td>
                </tr>
              }
              <tr class="discrepancy fw-semibold">
                <td>
                  Расхождение расчёт / факт
                  @if (provider.fact !== null) {
                    <span class="fw-normal small text-body-secondary">(расчёт за дни фактов — {{ money(provider.estimate) }})</span>
                  }
                </td>
                <td class="text-end text-nowrap">{{ provider.discrepancy === null ? '—' : signed(provider.discrepancy) }}</td>
                <td class="text-end text-nowrap fw-normal text-body-secondary">
                  {{ provider.discrepancy === null ? '' : ratio(provider.discrepancy, provider.fact ?? 0) }}
                </td>
              </tr>
            </tbody>
          </table>

          @if (provider.account.hasPayments) {
            <div class="small account">
              Счёт у поставщика: пополнено {{ money(provider.account.paid) }}
              · потрачено {{ money(provider.account.fact + provider.account.unbilled) }}
              @if (provider.account.unbilled) {
                <span class="text-body-secondary">(из них по расчёту за дни без факта — {{ money(provider.account.unbilled) }})</span>
              }
            </div>
            <div class="small" [class.text-danger]="provider.account.remaining <= 0">
              Остаток ≈ {{ money(provider.account.remaining) }}
            </div>
          } @else {
            <div class="small text-body-secondary">Платежей поставщику не записано — счёта нет.</div>
          }
          @if (canWrite()) {
            <div><button class="btn btn-sm btn-link p-0" type="button" (click)="openPayment(provider)" [disabled]="busy()">записать платёж</button></div>
          }

          @if (entry(); as form) {
            @if (form.provider === provider.provider) {
              <form class="row g-2 align-items-end mt-2 ledger-form" (ngSubmit)="saveEntry()">
                @if (form.kind === Kind.ActualSpend) {
                  <div class="col-auto">
                    <label class="form-label small mb-0" for="periodFrom">С</label>
                    <input class="form-control form-control-sm" id="periodFrom" name="periodFrom" type="date" [(ngModel)]="form.periodFrom">
                  </div>
                  <div class="col-auto">
                    <label class="form-label small mb-0" for="periodTo">по (включительно)</label>
                    <input class="form-control form-control-sm" id="periodTo" name="periodTo" type="date" [(ngModel)]="form.periodTo">
                  </div>
                }
                <div class="col-auto">
                  <label class="form-label small mb-0" for="amount">
                    {{ form.kind === Kind.ActualSpend ? 'Расход, ' + data.currency : 'Платёж, ' + data.currency + ' (минус — исправление)' }}
                  </label>
                  <input class="form-control form-control-sm" id="amount" name="amount" type="number" step="0.01"
                         style="width: 160px" [(ngModel)]="form.amount">
                </div>
                <div class="col">
                  <label class="form-label small mb-0" for="ledgerComment">Комментарий</label>
                  <input class="form-control form-control-sm" id="ledgerComment" name="comment" maxlength="500" [(ngModel)]="form.comment">
                </div>
                <div class="col-auto">
                  <button class="btn btn-sm btn-primary" type="submit" [disabled]="busy()">Записать</button>
                  <button class="btn btn-sm btn-outline-secondary ms-1" type="button" (click)="entry.set(null)" [disabled]="busy()">Отмена</button>
                </div>
              </form>
            }
          }
        </div>
      }

      <h3 class="h6">По моделям</h3>
      <table class="table table-sm bg-white small mb-3" style="max-width: 760px">
        <thead>
          <tr>
            <th>Модель</th>
            <th class="text-end">ходов</th>
            <th class="text-end">токенов</th>
            <th class="text-end">стоимость</th>
            <th class="text-end">служебные</th>
          </tr>
        </thead>
        <tbody>
          @for (model of data.models; track model.modelId) {
            <tr class="model-row" [class.table-active]="model.modelId === data.modelId">
              <td>{{ model.displayName }} <span class="text-body-secondary">· {{ model.provider }}</span></td>
              <td class="text-end">{{ whole(model.runs) }}</td>
              <td class="text-end text-nowrap">{{ tokens(model.tokens) }}</td>
              <td class="text-end text-nowrap">{{ money(model.cost) }}</td>
              <td class="text-end text-nowrap">
                {{ money(model.serviceCost) }}
                @if (model.serviceTokens) {
                  <span class="text-body-secondary">· {{ tokens(model.serviceTokens) }} ток.</span>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>

      <h3 class="h6">По пространствам</h3>
      <table class="table table-sm bg-white small" style="max-width: 760px">
        <thead>
          <tr>
            <th>Пространство</th>
            <th class="text-end">ходов</th>
            <th class="text-end">токенов</th>
            <th class="text-end">стоимость</th>
            <th class="text-end">лимит</th>
          </tr>
        </thead>
        <tbody>
          @for (workspace of data.workspaces; track workspace.workspaceId) {
            <tr class="workspace-row">
              <td>
                @if (workspace.workspaceName) {
                  <button class="btn btn-sm btn-link p-0 text-start" type="button" (click)="open(workspace.workspaceId)">{{ workspace.workspaceName }}</button>
                } @else {
                  <span class="text-body-secondary">удалено · {{ workspace.workspaceId }}</span>
                }
              </td>
              <td class="text-end">{{ whole(workspace.runs) }}</td>
              <td class="text-end text-nowrap">{{ tokens(workspace.tokens) }}</td>
              <td class="text-end text-nowrap">{{ money(workspace.cost) }}</td>
              <td class="text-end text-nowrap">{{ workspace.ownLimit ? 'свой' : 'по умолчанию' }}</td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="text-body-secondary">Ходов за период не было.</td></tr>
          }
        </tbody>
      </table>

      @if (card(); as opened) {
        <div class="card card-body bg-white mb-3 workspace-card" style="max-width: 760px">
          <div class="d-flex mb-2">
            <h3 class="h6 me-auto mb-0">{{ opened.workspaceName }}</h3>
            <button class="btn-close" type="button" aria-label="Закрыть" (click)="card.set(null)"></button>
          </div>
          <div class="small mb-2" id="card-usage">
            Сегодня: {{ tokens(opened.today.tokens) }} ток. · {{ money(opened.today.cost) }}
            · в этом месяце: {{ tokens(opened.month.tokens) }} ток. · {{ money(opened.month.cost) }}
          </div>
          <div class="small mb-2" id="card-limit">
            Лимит {{ opened.ownLimit ? 'свой' : 'по умолчанию' }}: {{ limitText(opened.limit) }}
            @if (opened.ownLimit && opened.limitUpdatedUtc) {
              <span class="text-body-secondary">· поставил {{ opened.limitUpdatedBy ?? '—' }}, {{ opened.limitUpdatedUtc | date: 'dd.MM.yyyy HH:mm' }}</span>
            }
          </div>
          <p class="small text-body-secondary mb-2">
            Лимит общий для всех участников: считается их расход вместе, за сутки и календарный месяц по UTC.
            Проверяется до начала хода — ход, начатый под лимитом, доходит до конца.
          </p>

          @if (canWrite()) {
            <form class="row g-2 align-items-end mb-3 limit-form" (ngSubmit)="saveLimit()">
              <div class="col-auto">
                <label class="form-label small mb-0" for="tokensPerDay">Токенов в сутки</label>
                <input class="form-control form-control-sm" id="tokensPerDay" name="tokensPerDay" type="number" min="0" style="width: 140px" [(ngModel)]="limit.tokensPerDay">
              </div>
              <div class="col-auto">
                <label class="form-label small mb-0" for="tokensPerMonth">в месяц</label>
                <input class="form-control form-control-sm" id="tokensPerMonth" name="tokensPerMonth" type="number" min="0" style="width: 140px" [(ngModel)]="limit.tokensPerMonth">
              </div>
              <div class="col-auto">
                <label class="form-label small mb-0" for="costPerDay">{{ opened.currency }} в сутки</label>
                <input class="form-control form-control-sm" id="costPerDay" name="costPerDay" type="number" min="0" step="0.01" style="width: 120px" [(ngModel)]="limit.costPerDay">
              </div>
              <div class="col-auto">
                <label class="form-label small mb-0" for="costPerMonth">в месяц</label>
                <input class="form-control form-control-sm" id="costPerMonth" name="costPerMonth" type="number" min="0" step="0.01" style="width: 120px" [(ngModel)]="limit.costPerMonth">
              </div>
              <div class="col-auto">
                <button class="btn btn-sm btn-primary" type="submit" id="save-limit" [disabled]="busy()">Поставить свой</button>
                @if (opened.ownLimit) {
                  <button class="btn btn-sm btn-outline-secondary ms-1" type="button" id="reset-limit" (click)="resetLimit()" [disabled]="busy()">Вернуть умолчание</button>
                }
              </div>
              <div class="col-12 small text-body-secondary">Пустое поле — без потолка.</div>
            </form>
          }

          <table class="table table-sm small mb-0">
            <thead>
              <tr>
                <th>Кто · {{ day(opened.from) }}–{{ day(opened.to) }}</th>
                <th class="text-end">ходов</th>
                <th class="text-end">токенов</th>
                <th class="text-end">стоимость</th>
              </tr>
            </thead>
            <tbody>
              @for (user of opened.users; track user.userId) {
                <tr class="user-row">
                  <td>{{ user.name ?? 'аккаунт удалён' }}</td>
                  <td class="text-end">{{ whole(user.runs) }}</td>
                  <td class="text-end text-nowrap">{{ tokens(user.tokens) }}</td>
                  <td class="text-end text-nowrap">{{ money(user.cost) }}</td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="text-body-secondary">За период никто не спрашивал.</td></tr>
              }
            </tbody>
          </table>
        </div>
      }
    }
  `
})
export class BillingComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly session = inject(AdminSessionService);

  protected readonly Kind = ProviderLedgerKind;

  protected readonly preset = signal<Preset>('last30');
  protected from = period('last30').from;
  protected to = period('last30').to;
  protected modelId: string | null = null;

  protected readonly data = signal<AdminBillingSummary | null>(null);
  protected readonly entry = signal<AdminLedgerEntry | null>(null);
  protected readonly card = signal<AdminBillingWorkspaceCard | null>(null);
  protected limit: AdminAssistantLimit = { tokensPerDay: null, tokensPerMonth: null, costPerDay: null, costPerMonth: null };

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Запись в счёт и лимиты — у роли «Расход» (спец. admin, 3.2) */
  protected readonly canWrite = computed(() => this.session.hasRole(AdminRole.Billing));

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async pick(preset: Preset): Promise<void> {
    this.preset.set(preset);
    if (preset === 'custom') return;

    ({ from: this.from, to: this.to } = period(preset));
    await this.load();
  }

  protected async load(): Promise<void> {
    if (!this.from || !this.to) return;
    await this.run(async () => this.data.set(await this.api.billingSummary(this.from, this.to, this.modelId)));
  }

  /** Карточка пространства — лимит и кто сколько потратил за тот же период */
  protected async open(workspaceId: string): Promise<void> {
    await this.run(async () => {
      const card = await this.api.billingWorkspace(workspaceId, this.from, this.to);
      this.limit = { ...(card.ownLimit ?? card.defaultLimit) };
      this.card.set(card);
    });
  }

  protected async saveLimit(): Promise<void> {
    const card = this.card();
    if (!card) return;

    const limit: AdminAssistantLimit = {
      tokensPerDay: limitOrNull(this.limit.tokensPerDay),
      tokensPerMonth: limitOrNull(this.limit.tokensPerMonth),
      costPerDay: limitOrNull(this.limit.costPerDay),
      costPerMonth: limitOrNull(this.limit.costPerMonth)
    };
    if (await this.run(() => this.api.setWorkspaceLimit(card.workspaceId, limit))) await this.reload(card.workspaceId);
  }

  protected async resetLimit(): Promise<void> {
    const card = this.card();
    if (!card) return;

    if (await this.run(() => this.api.resetWorkspaceLimit(card.workspaceId))) await this.reload(card.workspaceId);
  }

  /**
   * Факт: новый — на выбранный период (без дней, которые ещё не прошли), правка —
   * на период того факта: запись с тем же периодом заменяет прежнюю
   */
  protected openFact(provider: AdminBillingProvider, fact?: AdminLedgerFact): void {
    const yesterday = addDays(today(), -1);
    this.entry.set({
      provider: provider.provider,
      kind: ProviderLedgerKind.ActualSpend,
      amount: fact?.amount ?? 0,
      periodFrom: fact?.periodFrom ?? this.from,
      periodTo: fact?.periodTo ?? (this.to > yesterday ? yesterday : this.to),
      comment: fact?.comment ?? ''
    });
  }

  protected openPayment(provider: AdminBillingProvider): void {
    this.entry.set({
      provider: provider.provider,
      kind: ProviderLedgerKind.Payment,
      amount: 0,
      periodFrom: null,
      periodTo: null,
      comment: ''
    });
  }

  protected async saveEntry(): Promise<void> {
    const entry = this.entry();
    if (!entry) return;

    const saved = await this.run(async () => {
      await this.api.recordLedger({ ...entry, comment: entry.comment?.trim() || null });
    });
    if (!saved) return;

    // Форма закрывается сразу после записи, до перечитывания: иначе повторное
    // «Записать» завело бы второй платёж
    this.entry.set(null);
    await this.load();
  }

  protected limitText(limit: AdminAssistantLimit): string {
    const currency = this.data()?.currency ?? '';
    const parts = [
      limit.tokensPerDay === null ? null : `${this.tokens(limit.tokensPerDay)} ток. в сутки`,
      limit.tokensPerMonth === null ? null : `${this.tokens(limit.tokensPerMonth)} ток. в месяц`,
      limit.costPerDay === null ? null : `${this.money(limit.costPerDay)} ${currency} в сутки`,
      limit.costPerMonth === null ? null : `${this.money(limit.costPerMonth)} ${currency} в месяц`
    ].filter(x => x !== null);
    return parts.length ? parts.join(' · ') : 'без потолка';
  }

  protected serviceTitle(kind: ServiceSpendKind): string {
    return SERVICE_SPEND_TITLES[kind] ?? `вид ${kind}`;
  }

  protected money(value: number): string {
    return money.format(value);
  }

  protected signed(value: number): string {
    return (value > 0 ? '+' : value < 0 ? '−' : '') + this.money(Math.abs(value));
  }

  /** Доля со знаком в процентах: «(+15,6 %)»; от нулевой базы — пусто */
  protected ratio(value: number, base: number): string {
    if (!base) return '';
    const share = (value / base) * 100;
    return `(${share > 0 ? '+' : share < 0 ? '−' : ''}${percent.format(Math.abs(share))} %)`;
  }

  protected whole(value: number): string {
    return whole.format(value);
  }

  protected tokens(value: number): string {
    return value >= 1_000_000 ? `${millions.format(value / 1_000_000)} млн` : whole.format(value);
  }

  /** `yyyy-MM-dd` → `dd.MM.yyyy` без часового пояса: это день, а не момент */
  protected day(value: string): string {
    const [year, month, date] = value.split('-');
    return `${date}.${month}.${year}`;
  }

  /** Карточка и сводка после правки лимита: в сводке меняется пометка «свой» */
  private async reload(workspaceId: string): Promise<void> {
    await this.load();
    await this.open(workspaceId);
  }

  private async run(action: () => Promise<void>): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
      return true;
    } catch (error) {
      this.error.set(apiErrorText(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
