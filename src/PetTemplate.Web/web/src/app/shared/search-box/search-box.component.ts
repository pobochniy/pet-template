import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { injectI18n } from '../i18n/inject-i18n';

/**
 * Строка поиска над списком раздела (спец. search, 8): запрос уходит по «Найти»
 * или Enter, а не на каждую букву — каждый запрос считает вектор на эмбеддере. «Сбросить»
 * возвращает список целиком. Без `@angular/forms`: поле одно, и форма ему ни к чему.
 *
 * Экрана с ней в шаблоне нет — её ставит раздел проекта, когда подключает поиск к своей
 * сущности; импортом экрана она и попадает в его ленивый чанк, а не в initial. Подписи —
 * в `core`: строку рисуют разные разделы
 */
@Component({
  selector: 'shared-search-box',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="d-flex flex-wrap gap-2 mb-3" role="search" (submit)="submit($event)">
      <input #box type="search" class="form-control search-box" id="search-query" maxlength="200"
             [value]="query()" (input)="query.set(box.value)"
             [placeholder]="i18n.t('core.search.placeholder')" [attr.aria-label]="i18n.t('core.search.placeholder')" />
      <button type="submit" class="btn btn-outline-primary" id="search-submit" [disabled]="busy()">
        {{ i18n.t('core.search.submit') }}
      </button>
      @if (active()) {
        <button type="button" class="btn btn-outline-secondary" id="search-clear" (click)="clear()">
          {{ i18n.t('core.search.clear') }}
        </button>
      }
    </form>
  `,
  styles: `.search-box { max-width: 32rem; flex: 1 1 12rem; }`
})
export class SearchBoxComponent {
  protected readonly i18n = injectI18n();

  /** Поиск в пути — второй не запускаем */
  readonly busy = input(false);

  /** Непустой запрос, обрезанный по краям */
  readonly search = output<string>();

  /** Поиск сброшен — показать список целиком */
  readonly cleared = output<void>();

  protected readonly query = signal('');

  /** Выдача на экране — есть что сбрасывать */
  protected readonly active = signal(false);

  protected submit(event: Event): void {
    event.preventDefault();
    const query = this.query().trim();
    if (!query) {
      if (this.active()) this.clear();
      return;
    }

    this.active.set(true);
    this.search.emit(query);
  }

  protected clear(): void {
    this.query.set('');
    this.active.set(false);
    this.cleared.emit();
  }
}
