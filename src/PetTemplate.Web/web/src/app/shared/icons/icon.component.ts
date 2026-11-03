import { ChangeDetectionStrategy, Component, ElementRef, Renderer2, effect, inject, input } from '@angular/core';
import type { IconNode } from 'lucide';

const SVG_NS = 'http://www.w3.org/2000/svg';

/**
 * Иконка lucide: `<svg [appIcon]="icons.Mic" [size]="18">`. Данные — из ванильного
 * пакета `lucide`, поштучно (`import Mic from 'lucide/dist/esm/icons/mic.mjs'`), а
 * компонент свой, без обёртки для Angular (`tech-debt/bundle.md`, `src/PetTemplate.Web/agents.md`, «Иконки»):
 * - в `lucide` каждая иконка — свой модуль, и esbuild кладёт её в чанк экрана, который её
 *   показывает. Обёртки (`lucide-angular`, `@lucide/angular`) — один файл: любая взятая из них
 *   иконка уезжает в initial рядом с компонентом оболочки;
 * - `@lucide/angular` — ≈10 КБ ядра в initial, а его поштучные компоненты `LucideX` несут по
 *   своей копии шаблона, ≈3.3 КБ на иконку (замер 2026-11-03). Здесь — один `createElementNS`.
 *
 * Атрибуты корня — `defaultAttributes` lucide. По умолчанию `aria-hidden`: иконки шаблона
 * стоят рядом с текстом или в кнопке со своей `aria-label`; смысловой иконке — `aria-hidden="false"`
 * и `role="img"` с подписью
 */
@Component({
  selector: 'svg[appIcon]',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'lucide',
    xmlns: SVG_NS,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    'stroke-width': '2',
    'stroke-linecap': 'round',
    'stroke-linejoin': 'round',
    'aria-hidden': 'true',
    '[attr.width]': 'size()',
    '[attr.height]': 'size()'
  }
})
export class IconComponent {
  readonly appIcon = input.required<IconNode>();
  readonly size = input<number | string>(24);

  private readonly host = inject<ElementRef<SVGSVGElement>>(ElementRef).nativeElement;
  private readonly renderer = inject(Renderer2);

  constructor() {
    effect(() => {
      const node = this.appIcon();

      for (const child of Array.from(this.host.childNodes)) this.renderer.removeChild(this.host, child);
      for (const [tag, attrs] of node) {
        const element = this.renderer.createElement(tag, SVG_NS);
        for (const [name, value] of Object.entries(attrs)) {
          if (value != null) this.renderer.setAttribute(element, name, String(value));
        }
        this.renderer.appendChild(this.host, element);
      }
    });
  }
}
