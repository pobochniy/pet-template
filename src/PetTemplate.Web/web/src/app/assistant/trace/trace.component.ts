import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { AssistantApiService } from '../../shared/api/assistant-api.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { formatNumber } from '../../shared/i18n/format.util';
import { AssistantTrace } from '../../shared/models/assistant.model';

/**
 * «Показать ход мысли» (спец. assistant, 13.2): шаги хода в читаемом виде —
 * «искал в справке: как пригласить коллегу», — без JSON и без служебных полей.
 * Строку шага собирает сервер, а не модель.
 *
 * Это объясняет результат и делает ошибку видимой: «он искал не то».
 *
 * Трасса грузится по клику, а не вместе с лентой: у неё другой объём и другой
 * срок жизни, и большинству сообщений она не нужна никогда.
 */
@Component({
  selector: 'app-assistant-trace',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" class="btn btn-sm btn-link p-0 trace__toggle" (click)="toggle()">
      {{ i18n.t(open() ? 'assistant.trace.hide' : 'assistant.trace.show') }}
    </button>

    @if (open()) {
      @if (trace(); as loaded) {
        @if (loaded.expired || loaded.steps.length === 0) {
          <div class="trace__empty small text-body-secondary">{{ i18n.t('assistant.trace.expired') }}</div>
        } @else {
          <ol class="trace__steps small text-body-secondary ps-3 mb-0 mt-1">
            @for (step of loaded.steps; track step.step) {
              <li class="trace__step" [class.text-danger]="!!step.error">
                {{ step.kind === 'model' ? i18n.t('assistant.trace.model') : step.summary }}
                <span class="trace__duration">({{ seconds(step.durationMs) }})</span>
                @if (step.error) {
                  <span class="trace__error"> — {{ step.error }}</span>
                }
              </li>
            }
          </ol>
        }
      }
    }
  `
})
export class AssistantTraceComponent {
  private readonly api = inject(AssistantApiService);
  protected readonly i18n = inject(I18nService);

  public readonly runId = input.required<string>();

  public readonly open = signal(false);

  public readonly trace = signal<AssistantTrace | null>(null);

  public async toggle() {
    if (this.open()) {
      this.open.set(false);

      return;
    }

    this.open.set(true);
    // Грузим один раз: ход закончен, и меняться его трассе больше не с чего
    if (this.trace() == null) await this.load();
  }

  /** Длительность — в секундах: миллисекунды человеку ничего не говорят */
  public seconds(ms: number) {
    const value = formatNumber(this.i18n.lang(), ms / 1000, { minimumFractionDigits: 1, maximumFractionDigits: 1 });

    return this.i18n.t('assistant.trace.seconds', { value });
  }

  private async load() {
    try {
      this.trace.set(await this.api.Trace(this.runId()));
    } catch {
      // Трассы нет — это не поломка экрана: скажем то же, что и про вычищенную
      this.trace.set({ expired: true, steps: [] });
    }
  }
}
