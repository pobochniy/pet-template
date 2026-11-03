import {
  ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, output, signal,
  viewChild
} from '@angular/core';
import Search from 'lucide/dist/esm/icons/search.mjs';
import X from 'lucide/dist/esm/icons/x.mjs';
import { IconComponent } from '../icons/icon.component';
import { LangFlagComponent } from '../i18n/flags/lang-flag.component';
import { Lang, LangMeta, SUPPORTED_LANGUAGES } from '../i18n/lang';
import { injectI18n } from '../i18n/inject-i18n';

/**
 * Выбор языка интерфейса: флаг и самоназвание, клик меняет язык и закрывает окно.
 *
 * Подписываем **самоназванием**, а не переводом названия языка: человек, который
 * ищет свой язык, узнаёт его именно в таком виде — «Русский» и «English», а не
 * «Russian» посреди английского интерфейса. Флаг остаётся украшением: флаг —
 * это страна, а не язык.
 */
@Component({
  selector: 'shared-language-picker',
  templateUrl: './language-picker.component.html',
  styleUrl: './language-picker.component.css',
  standalone: true,
  imports: [IconComponent, LangFlagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    // Escape закрывает окно из любого места — фокус к этому моменту стоит в поиске
    '(document:keydown.escape)': 'onEscape()'
  }
})
export class LanguagePickerComponent {
  protected readonly icons = { Search, X };

  protected readonly i18n = injectI18n();

  isOpen = input.required<boolean>();

  /** Текущий язык — он подсвечен в сетке */
  selected = input.required<Lang>();

  picked = output<Lang>();
  closed = output<void>();

  protected readonly query = signal('');

  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  /**
   * Поле поиска показываем только когда языков больше горстки: при двух оно
   * занимает половину окна и ничего не ускоряет. Логика фильтрации написана
   * сразу — скрыто только поле, и третий язык не потребует правок
   */
  protected readonly showSearch = SUPPORTED_LANGUAGES.length > 6;

  /** Ищем по самоназванию, английскому названию и коду: «ru», «Русский», «Russian» */
  protected readonly visibleLanguages = computed<readonly LangMeta[]>(() => {
    const query = this.query().trim().toLowerCase();
    if (!query) return SUPPORTED_LANGUAGES;

    return SUPPORTED_LANGUAGES.filter(
      lang =>
        lang.nativeName.toLowerCase().includes(query) ||
        lang.englishName.toLowerCase().includes(query) ||
        lang.code.includes(query)
    );
  });

  constructor() {
    effect(() => {
      if (!this.isOpen()) return;

      // Поиск чистим на каждом открытии: прошлый запрос показывал бы один язык
      // вместо списка, и было бы непонятно, куда делись остальные
      this.query.set('');

      // Фокус ставим после отрисовки: до неё поля ещё нет
      queueMicrotask(() => this.searchInput()?.nativeElement.focus());
    });
  }

  protected onQueryInput(value: string): void {
    this.query.set(value);
  }

  protected onPick(lang: LangMeta): void {
    this.picked.emit(lang.code);
  }

  protected onEscape(): void {
    if (this.isOpen()) this.closed.emit();
  }

  protected onClose(): void {
    this.closed.emit();
  }
}
