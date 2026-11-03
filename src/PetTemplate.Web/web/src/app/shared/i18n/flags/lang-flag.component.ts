import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Lang } from '../lang';

/**
 * Флаг языка.
 *
 * Inline-SVG, а не эмодзи: на Windows флаговые эмодзи рисуются как буквы «RU»
 * и «GB» — шрифта с флагами в системе нет, и переключатель превратился бы
 * в две пары букв.
 *
 * И не `<img src="…svg">`: это отдельный запрос на каждый флаг и мигание
 * при открытии переключателя, а всё содержимое — десяток прямоугольников.
 * `[innerHTML]` тоже мимо: разметку из переменной санитайзер Angular режет.
 *
 * **Флаг — это страна, а не язык**, поэтому он остаётся украшением: подписан
 * пункт самоназванием (`SUPPORTED_LANGUAGES`), и `aria-hidden` здесь именно
 * поэтому — читать вслух картинку нечего.
 */
@Component({
  selector: 'shared-lang-flag',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (lang()) {
      @case ('ru') {
        <svg viewBox="0 0 24 16" class="lang-flag" aria-hidden="true" focusable="false">
          <rect width="24" height="16" fill="#fff" />
          <rect y="5.33" width="24" height="5.34" fill="#0039a6" />
          <rect y="10.67" width="24" height="5.33" fill="#d52b1e" />
          <rect width="24" height="16" fill="none" stroke="rgba(0,0,0,.15)" stroke-width="1" />
        </svg>
      }
      @case ('en') {
        <!-- Юнион Джек: диагонали даны толстой белой и тонкой красной линией
             поверх синего поля — точная геральдика в 24×16 не читается -->
        <svg viewBox="0 0 24 16" class="lang-flag" aria-hidden="true" focusable="false">
          <rect width="24" height="16" fill="#012169" />
          <path d="M0 0 24 16M24 0 0 16" stroke="#fff" stroke-width="3.2" />
          <path d="M0 0 24 16M24 0 0 16" stroke="#c8102e" stroke-width="1.6" />
          <path d="M12 0v16M0 8h24" stroke="#fff" stroke-width="5.3" />
          <path d="M12 0v16M0 8h24" stroke="#c8102e" stroke-width="3.2" />
          <rect width="24" height="16" fill="none" stroke="rgba(0,0,0,.15)" stroke-width="1" />
        </svg>
      }
    }
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    .lang-flag {
      width: 1.5rem;
      height: 1rem;
      border-radius: 0.125rem;
    }
  `
})
export class LangFlagComponent {
  lang = input.required<Lang>();
}
