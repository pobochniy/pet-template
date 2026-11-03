import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { ASSISTANT_SCOPES } from '../assistant.scopes';

/**
 * Плашка «лимит пространства исчерпан» на месте поля ввода ассистента (спец. assistant,
 * 13.6): на главной она встаёт вместо строки ввода, в чате — вместо формы внизу,
 * не сдвигая ленту. Ассистент не исчезает — исчезает возможность отправить.
 *
 * Текст — перевод отказа сервера: какой лимит кончился, в каком пространстве и когда
 * откроется. Вторая строка говорит, что лимит **общий** для всех участников: без неё
 * «у меня же ничего не было» читается как поломка. «Понятно» возвращает поле ввода —
 * дневной лимит откроется завтра, а следующий отказ поставит плашку снова
 */
@Component({
  selector: 'app-limit-reached',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="limit-reached d-flex align-items-start gap-2" role="status">
      <div class="flex-grow-1">
        <div class="limit-reached__text">{{ text() }}</div>
        <div class="small text-body-secondary">{{ i18n.t('assistant.limit.shared') }}</div>
      </div>
      <button type="button" class="btn btn-sm btn-outline-secondary flex-shrink-0 limit-reached__dismiss"
              (click)="dismiss.emit()">
        {{ i18n.t('assistant.limit.dismiss') }}
      </button>
    </div>
  `
})
export class LimitReachedComponent {
  protected readonly i18n = injectI18n(...ASSISTANT_SCOPES);

  public readonly text = input.required<string>();

  public readonly dismiss = output<void>();
}
