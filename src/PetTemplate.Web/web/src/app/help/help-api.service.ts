import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BaseApiService, toPromise } from '../shared/api/base-api.service';

/** Статья в списке справки */
export interface HelpArticleSummary {
  readonly slug: string;
  readonly title: string;
}

/** Статья целиком: тело — markdown разделов `##` (спец. help, 3.2) */
export interface HelpArticle extends HelpArticleSummary {
  readonly markdown: string;
}

/**
 * Справка из API (`GET api/Help`, `GET api/Help/{slug}`) — та же опубликованная версия,
 * по которой отвечает ассистент (спец. help, 6.4). Без входа: справка общая.
 * Лежит в разделе справки — больше она никому не нужна
 */
@Injectable({ providedIn: 'root' })
export class HelpApiService extends BaseApiService {
  constructor(http: HttpClient) {
    super('Help', http);
  }

  async list(signal?: AbortSignal): Promise<HelpArticleSummary[]> {
    // `/api/Help` без хвостового `/`: `get('')` дал бы `/api/Help/`
    return (await toPromise(this.http.get<HelpArticleSummary[]>(`/api/${this.apiName}`), signal)) ?? [];
  }

  async article(slug: string, signal?: AbortSignal): Promise<HelpArticle> {
    return this.get<HelpArticle>(encodeURIComponent(slug), signal);
  }
}
