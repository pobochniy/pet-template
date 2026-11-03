import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import {
  AssistantPlan, AssistantPlanStatus, PlanOperation, PlanOperationResult
} from '../../shared/models/assistant.model';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { ASSISTANT_SCOPES } from '../assistant.scopes';

/** Строка блока плана — уже готовая к показу, без логики в шаблоне */
export interface PlanRow {
  index: number;
  text: string;
  /** Галочка, крестик или ничего: до операции могло не дойти (5.1) */
  mark: 'none' | 'done' | 'failed';
  error?: string;
}

/**
 * Блок плана (спец. assistant, 5.1). Строится <b>детерминированно</b> из JSON плана,
 * а не из прозы модели: иначе гарантия «создастся ровно то, что прочитал» держалась бы
 * на том, что модель обычно не врёт.
 *
 * Тот же блок рисует отчёт «Готово» — разница только в статусе плана и значках
 * у строк. Отчёт приезжает в сообщении после согласия; у предложения `result` пуст.
 *
 * Видов операций в ядре нет — их приносят разделы проекта (7.1). Строка вида — ключ
 * `assistant.plan.kind.{Kind}` словаря ассистента, подстановки — из аргументов
 * операции: `"CreateNote": "заметка «{title}»"`. Вида нет в словаре — строка
 * показывает первый текстовый аргумент (обычно название), а без него — сам вид:
 * лучше сухая строка, чем пропавшая операция.
 */
@Component({
  selector: 'app-plan-block',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="plan-block small" [class.plan-block--cancelled]="cancelled()">
      @if (draft()) {
        <div class="plan-block__title text-body-secondary">{{ i18n.t('assistant.plan.draft') }}</div>
      }
      @if (applied()) {
        <div class="plan-block__title">{{ i18n.t('assistant.plan.done') }}</div>
      }

      @for (row of visibleRows(); track row.index) {
        <div class="plan-block__row">
          <span class="plan-block__mark" [class.text-success]="row.mark === 'done'"
                [class.text-danger]="row.mark === 'failed'">
            @switch (row.mark) {
              @case ('done') { ✓ }
              @case ('failed') { ✗ }
            }
          </span>
          <span>{{ row.text }}</span>
          @if (row.error) {
            <span class="plan-block__error text-danger"> — {{ row.error }}</span>
          }
        </div>
      }

      @if (hasHidden()) {
        <button type="button" class="btn btn-sm btn-link p-0" (click)="expanded.set(true)">
          {{ i18n.t('assistant.plan.more', { count: hiddenCount() }) }}
        </button>
      }
    </div>
  `,
  styles: [`
    .plan-block { border-left: 2px solid var(--bs-border-color); padding-left: 0.5rem; }
    .plan-block__mark { display: inline-block; min-width: 1rem; }
    /* Отменённый план остаётся в ленте текстом: человек видит, что предлагалось (5.1) */
    .plan-block--cancelled { opacity: 0.6; }
  `]
})
export class PlanBlockComponent {
  protected readonly i18n = injectI18n(...ASSISTANT_SCOPES);

  /** Сколько строк показывать, пока человек не раскрыл остальные */
  private static readonly Visible = 10;

  public readonly plan = input.required<AssistantPlan>();

  public readonly expanded = signal(false);

  public readonly rows = computed<PlanRow[]>(() => {
    const plan = this.plan();
    const results = plan.result?.operations ?? [];

    return plan.operations.operations
      .slice()
      .sort((a, b) => a.index - b.index)
      .map(operation => this.row(operation, results.find(x => x.index === operation.index)));
  });

  /** Видимая часть: длинный отчёт сворачивается, чтобы лента не превращалась в простыню */
  public readonly visibleRows = computed(() =>
    this.expanded() ? this.rows() : this.rows().slice(0, PlanBlockComponent.Visible));

  public readonly hiddenCount = computed(() => Math.max(this.rows().length - PlanBlockComponent.Visible, 0));

  public readonly hasHidden = computed(() => this.hiddenCount() > 0 && !this.expanded());

  public readonly applied = computed(() => this.plan().status === AssistantPlanStatus.Applied);

  public readonly cancelled = computed(() => this.plan().status === AssistantPlanStatus.Cancelled);

  public readonly draft = computed(() => this.plan().status === AssistantPlanStatus.Draft);

  private row(operation: PlanOperation, result?: PlanOperationResult): PlanRow {
    return {
      index: operation.index,
      text: this.text(operation),
      mark: result == null ? 'none' : result.done ? 'done' : 'failed',
      error: result?.error ?? undefined
    };
  }

  private text(operation: PlanOperation): string {
    const params: Record<string, string | number> = {};
    for (const [name, value] of Object.entries(operation.args ?? {})) {
      if (typeof value === 'string' || typeof value === 'number') params[name] = value;
    }

    return this.i18n.translateCode(`assistant.plan.kind.${operation.kind}`, params)
      ?? Object.values(params).find((x): x is string => typeof x === 'string' && x.length > 0)
      ?? operation.kind;
  }
}
