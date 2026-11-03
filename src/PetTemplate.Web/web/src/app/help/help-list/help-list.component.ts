import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HelpApiService, HelpArticleSummary } from '../help-api.service';
import { isAbortError } from '../../shared/api/base-api.service';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { HELP_SCOPES } from '../help.scopes';

type Status = 'loading' | 'ready' | 'error';

/**
 * Список статей справки (спец. help, 6.5). Справка не зависит ни от пользователя,
 * ни от пространства — поэтому не `contextResource` (он без активного пространства
 * не грузит вовсе, а справку читают и гости), а свой запрос с отменой при уходе с экрана
 */
@Component({
  selector: 'app-help-list',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container py-3" style="max-width: 48rem">
      <h1 class="h3">{{ i18n.t('help.title') }}</h1>
      <p class="text-body-secondary">{{ i18n.t('help.intro') }}</p>

      @switch (status()) {
        @case ('loading') {
          <p class="text-body-secondary" id="help-loading">{{ i18n.t('core.empty.loading') }}</p>
        }
        @case ('error') {
          <div class="alert alert-danger d-flex flex-wrap align-items-center gap-2" id="help-error">
            <span class="flex-grow-1">{{ i18n.t('help.failed') }}</span>
            <button type="button" class="btn btn-sm btn-outline-danger" (click)="load()">{{ i18n.t('core.action.retry') }}</button>
          </div>
        }
        @default {
          @if (articles().length === 0) {
            <p class="text-body-secondary" id="help-empty">{{ i18n.t('help.empty') }}</p>
          } @else {
            <ul class="list-unstyled" id="help-articles">
              @for (article of articles(); track article.slug) {
                <li class="mb-2"><a [routerLink]="['/help', article.slug]">{{ article.title }}</a></li>
              }
            </ul>
          }
        }
      }
    </div>
  `
})
export class HelpListComponent {
  protected readonly i18n = injectI18n(...HELP_SCOPES);

  protected readonly articles = signal<HelpArticleSummary[]>([]);
  protected readonly status = signal<Status>('loading');

  private controller: AbortController | null = null;

  constructor(private api: HelpApiService) {
    inject(DestroyRef).onDestroy(() => this.controller?.abort());
    void this.load();
  }

  protected async load(): Promise<void> {
    this.controller?.abort();
    const controller = (this.controller = new AbortController());
    this.status.set('loading');

    try {
      const articles = await this.api.list(controller.signal);
      this.articles.set(articles);
      this.status.set('ready');
    } catch (e) {
      if (!isAbortError(e)) this.status.set('error');
    }
  }
}
