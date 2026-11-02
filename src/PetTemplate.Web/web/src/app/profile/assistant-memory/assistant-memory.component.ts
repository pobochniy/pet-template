import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AssistantApiService } from '../../shared/api/assistant-api.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { I18nKey } from '../../shared/i18n/generated/i18n-keys';
import { formatDate } from '../../shared/i18n/format.util';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { AssistantFact, AssistantMemory } from '../../shared/models/assistant.model';

/** Сколько держится всплывающая ошибка */
const ALERT_MS = 5000;

/**
 * Что ассистент помнит (спец. assistant, 12.5).
 *
 * <b>Память обязана быть видимой:</b> это одновременно ответ про приватность
 * и про корректность — неверный факт удаляют, а не тащат вечно.
 *
 * Память — в пространстве, как и тред: экран показывает активное пространство
 * и перечитывается при его смене в шапке. Редактирования текста и добавления руками
 * здесь нет: неверный факт удаляют и говорят ассистенту правильно. Удаление —
 * с подтверждением в той же строке, как везде в приложении.
 */
@Component({
  selector: 'app-assistant-memory',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="container py-4">
      <div class="d-flex justify-content-end mb-3">
        <a [routerLink]="['/profile/edit']" class="btn btn-outline-secondary">{{ i18n.t('assistant.memory.back') }}</a>
      </div>

      <div class="row justify-content-center">
        <div class="col-lg-7">
          <h1 class="h4 mb-2">{{ i18n.t('assistant.memory.title') }}</h1>
          <p class="small text-body-secondary mb-4">{{ i18n.t('assistant.memory.hint') }}</p>

          @if (loaded() && empty()) {
            <p class="text-body-secondary" id="memory-empty">{{ i18n.t('assistant.memory.empty') }}</p>
          }

          @for (group of groups(); track group.id) {
            @if (group.facts.length > 0) {
              <section class="mb-4" [attr.data-group]="group.id">
                <h2 class="h6 text-body-secondary">{{ i18n.t(group.title) }}</h2>

                <ul class="list-unstyled mb-0">
                  @for (fact of group.facts; track fact.id) {
                    <li class="memory__row d-flex flex-wrap align-items-center gap-2 py-2 border-bottom">
                      <span class="memory__text flex-grow-1">{{ fact.text }}</span>
                      <span class="small text-body-secondary">{{ date(fact) }}</span>

                      @if (fact.sourceRunId) {
                        <a class="small" [routerLink]="['/assistant']">{{ i18n.t('assistant.memory.source') }}</a>
                      }

                      @if (forgetting() === fact.id) {
                        <span class="small text-danger">{{ i18n.t('assistant.memory.forgetConfirm') }}</span>
                        <button type="button" class="btn btn-sm btn-link text-danger p-0 fact-forget-confirm"
                                (click)="forget(fact.id)">
                          {{ i18n.t('assistant.memory.forget') }}
                        </button>
                        <button type="button" class="btn btn-sm btn-link p-0" (click)="forgetting.set(null)">
                          {{ i18n.t('core.action.cancel') }}
                        </button>
                      } @else {
                        <button type="button" class="btn btn-sm btn-link text-danger p-0 fact-forget"
                                (click)="forgetting.set(fact.id)">
                          {{ i18n.t('assistant.memory.forget') }}
                        </button>
                      }
                    </li>
                  }
                </ul>
              </section>
            }
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    .memory__text {
      overflow-wrap: anywhere;
    }
  `]
})
export class AssistantMemoryComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AssistantApiService);
  private readonly alerts = inject(AlertsService);
  private readonly workspaceContext = inject(WorkspaceContextService);

  public readonly memory = signal<AssistantMemory>({ shared: [], personal: [] });

  /** Ответ пришёл: «пусто» до него было бы враньём */
  public readonly loaded = signal(false);

  /** Факт, удаление которого ждёт подтверждения */
  public readonly forgetting = signal<string | null>(null);

  /** Плоский список, сгруппированный по владельцу — «Про пространство» и «Про меня» (12.5) */
  public readonly groups = computed<{ id: string; title: I18nKey; facts: AssistantFact[] }[]>(() => [
    { id: 'shared', title: 'assistant.memory.shared', facts: this.memory().shared },
    { id: 'personal', title: 'assistant.memory.personal', facts: this.memory().personal }
  ]);

  public readonly empty = computed(() =>
    this.memory().shared.length === 0 && this.memory().personal.length === 0);

  constructor() {
    // Память — своя в каждом пространстве: сменили его в шапке — перечитываем
    effect(() => {
      this.workspaceContext.currentWorkspaceId();
      untracked(() => {
        this.forgetting.set(null);
        void this.load();
      });
    });
  }

  public date(fact: AssistantFact) {
    return formatDate(this.i18n.lang(), fact.createdAtUtc);
  }

  public async forget(id: string) {
    this.forgetting.set(null);
    try {
      await this.api.DeleteFact(id);
      this.memory.update(current => ({
        shared: current.shared.filter(x => x.id !== id),
        personal: current.personal.filter(x => x.id !== id)
      }));
    } catch (e) {
      this.alerts.push('danger', serverErrorMessage(e, this.i18n, this.i18n.t('assistant.error.unknown')), ALERT_MS);
    }
  }

  private async load() {
    try {
      this.memory.set(await this.api.Memory());
      this.loaded.set(true);
    } catch (e) {
      this.alerts.push('danger', serverErrorMessage(e, this.i18n, this.i18n.t('assistant.error.unknown')), ALERT_MS);
    }
  }
}
