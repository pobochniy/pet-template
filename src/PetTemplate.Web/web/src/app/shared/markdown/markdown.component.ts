import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink, UrlTree } from '@angular/router';
import { MdInline, MdList, parseMarkdown } from './markdown';

/**
 * Строчные узлы — внутри элемента, на котором стоит атрибут: `<strong [mdInlines]="…">`.
 * Рекурсия — самим компонентом, а не `ngTemplateOutlet`: код `NgTemplateOutlet` лежит
 * в общем с оболочкой файле `@angular/common`, и esbuild положил бы его в initial
 * (+1.2 КБ, `tech-debt/bundle.md`)
 */
@Component({
  selector: '[mdInlines]',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (node of mdInlines(); track $index) {
      @switch (node.kind) {
        @case ('text') {{{ node.text }}}
        @case ('break') {<br />}
        @case ('code') {<code>{{ node.text }}</code>}
        @case ('strong') {<strong [mdInlines]="node.children"></strong>}
        @case ('em') {<em [mdInlines]="node.children"></em>}
        @case ('link') {<a [routerLink]="route(node.href)" [mdInlines]="node.children"></a>}
      }
    }
  `
})
export class MdInlinesComponent {
  readonly mdInlines = input.required<readonly MdInline[]>();

  private readonly router = inject(Router);
  private readonly routes = new Map<string, UrlTree>();

  /**
   * Адрес ссылки — деревом роутера, а не строкой: строку `routerLink` считает одним
   * сегментом и закодировал бы `?` и `#`. Кеш — чтобы ссылка не менялась на каждой отрисовке
   */
  protected route(href: string): UrlTree {
    let tree = this.routes.get(href);
    if (!tree) this.routes.set(href, (tree = this.router.parseUrl(href)));
    return tree;
  }
}

/** Пункты списка — внутри `<ul>`/`<ol>`; вложенный список — тем же компонентом */
@Component({
  selector: 'ul[mdList], ol[mdList]',
  imports: [MdInlinesComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (item of mdList().items; track $index) {
      <li>
        <span [mdInlines]="item.inlines"></span>
        @for (child of item.children; track $index) {
          @if (child.ordered) {
            <ol class="mb-0" [mdList]="child" [attr.start]="child.start === 1 ? null : child.start"></ol>
          } @else {
            <ul class="mb-0" [mdList]="child"></ul>
          }
        }
      </li>
    }
  `
})
export class MdListComponent {
  readonly mdList = input.required<MdList>();
}

/**
 * Markdown ответа ассистента и статьи справки — узлами шаблона, без `innerHTML`
 * (`markdown.ts`): текст узлов экранирует интерполяция Angular, ссылки — только `routerLink`
 * на свой маршрут. Лежит в `shared/`, потому что нужен двум разделам — чату и справке;
 * в initial не попадает: его импортируют только ленивые экраны.
 *
 * Заголовки статьи — `h2`…`h4` по уровню `##`; `#` — тоже `h2`: заголовок страницы
 * рисует сам экран, второй `h1` ему не нужен
 */
@Component({
  selector: 'app-markdown',
  imports: [MdInlinesComponent, MdListComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'markdown d-block' },
  styles: `
    :host > :last-child { margin-bottom: 0; }
    pre { white-space: pre-wrap; overflow-wrap: anywhere; background: var(--bs-tertiary-bg); }
  `,
  template: `
    @for (block of blocks(); track $index) {
      @switch (block.kind) {
        @case ('paragraph') {
          <p class="mb-2" [mdInlines]="block.inlines"></p>
        }
        @case ('heading') {
          @if (block.level <= 2) {
            <h2 class="h5 mt-3 mb-2" [mdInlines]="block.inlines"></h2>
          } @else if (block.level === 3) {
            <h3 class="h6 mt-3 mb-2" [mdInlines]="block.inlines"></h3>
          } @else {
            <h4 class="h6 mt-2 mb-1" [mdInlines]="block.inlines"></h4>
          }
        }
        @case ('code') {
          <pre class="mb-2 p-2 rounded small"><code>{{ block.text }}</code></pre>
        }
        @case ('list') {
          @if (block.ordered) {
            <ol class="mb-2" [mdList]="block" [attr.start]="block.start === 1 ? null : block.start"></ol>
          } @else {
            <ul class="mb-2" [mdList]="block"></ul>
          }
        }
      }
    }
  `
})
export class MarkdownComponent {
  /** Исходный markdown — ответ модели или тело статьи */
  readonly text = input.required<string>();

  protected readonly blocks = computed(() => parseMarkdown(this.text()));
}
