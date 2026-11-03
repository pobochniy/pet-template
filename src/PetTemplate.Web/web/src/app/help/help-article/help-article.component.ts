import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { HelpApiService } from '../help-api.service';
import { readerMarkdown } from '../help-text';
import { isAbortError } from '../../shared/api/base-api.service';
import { MarkdownComponent } from '../../shared/markdown/markdown.component';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { HELP_SCOPES } from '../help.scopes';

type Status = 'loading' | 'ready' | 'notFound' | 'error';

/**
 * Статья справки (спец. help, 3.2 и 6.5): название — заголовком страницы, тело — markdown
 * тем же отрисовщиком, что ответы ассистента (`shared/markdown`): ссылки только на свои
 * страницы. Строки «Как спрашивают:» человеку не показываются (`readerMarkdown`).
 *
 * `slug` — параметр адреса (`withComponentInputBinding`): переход на соседнюю статью
 * тот же экран не пересоздаёт, поэтому запрос идёт за сменой входа, прежний обрывается
 */
@Component({
  selector: 'app-help-article',
  imports: [RouterLink, MarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container py-3" style="max-width: 48rem">
      <a routerLink="/help" class="small" id="help-back">{{ i18n.t('help.back') }}</a>

      @switch (status()) {
        @case ('loading') {
          <p class="text-body-secondary mt-3" id="help-loading">{{ i18n.t('core.empty.loading') }}</p>
        }
        @case ('notFound') {
          <p class="mt-3" id="help-not-found">{{ i18n.t('help.notFound') }}</p>
        }
        @case ('error') {
          <div class="alert alert-danger d-flex flex-wrap align-items-center gap-2 mt-3" id="help-error">
            <span class="flex-grow-1">{{ i18n.t('help.failed') }}</span>
            <button type="button" class="btn btn-sm btn-outline-danger" (click)="load(slug())">{{ i18n.t('core.action.retry') }}</button>
          </div>
        }
        @default {
          <h1 class="h3 mt-2" id="help-title">{{ title() }}</h1>
          <app-markdown id="help-body" [text]="body()" />
        }
      }
    </div>
  `
})
export class HelpArticleComponent {
  protected readonly i18n = injectI18n(...HELP_SCOPES);

  readonly slug = input.required<string>();

  protected readonly status = signal<Status>('loading');
  protected readonly title = signal('');
  protected readonly body = signal('');

  private controller: AbortController | null = null;

  constructor(private api: HelpApiService) {
    inject(DestroyRef).onDestroy(() => this.controller?.abort());

    effect(() => {
      const slug = this.slug();
      untracked(() => void this.load(slug));
    });
  }

  protected async load(slug: string): Promise<void> {
    this.controller?.abort();
    const controller = (this.controller = new AbortController());
    this.status.set('loading');

    try {
      const article = await this.api.article(slug, controller.signal);
      this.title.set(article.title);
      this.body.set(readerMarkdown(article.markdown ?? ''));
      this.status.set('ready');
    } catch (e) {
      if (isAbortError(e)) return;
      this.status.set(e instanceof HttpErrorResponse && e.status === 404 ? 'notFound' : 'error');
    }
  }
}
