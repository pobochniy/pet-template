import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { HelpApiService, HelpArticle, HelpArticleSummary } from './help-api.service';
import { helpRoutes } from './help.routes';
import { readerMarkdown } from './help-text';
import { I18nService } from '../shared/i18n/i18n.service';
import { loadAllScopes } from '../shared/i18n/i18n.testing';

/**
 * Справка на сайте (спец. help, 6.5). Мок повторяет правила сервера: список — названия
 * и `slug`, статья — markdown как он лежит в `help/`, неизвестный `slug` — 404
 */
class HelpApiServiceMock {
  articles: HelpArticle[] = [
    {
      slug: 'workspace',
      title: 'Пространство и участники',
      markdown: '---\ntitle: Пространство и участники\npage: /workspace/settings\n---\n\n'
        + '## Как пригласить человека\n\nОткройте [страницу участников](/workspace/members) и нажмите **Пригласить**.\n'
        + '- ссылка живёт неделю\n- её можно отозвать\n\n'
        + 'Как спрашивают: как пригласить коллегу; где ссылка\n'
    },
    { slug: 'account', title: 'Аккаунт', markdown: '## Вход\n\nТекст.' }
  ];
  failList = false;

  async list(): Promise<HelpArticleSummary[]> {
    if (this.failList) throw new HttpErrorResponse({ status: 503 });
    return this.articles.map(({ slug, title }) => ({ slug, title }));
  }

  async article(slug: string): Promise<HelpArticle> {
    const article = this.articles.find(x => x.slug === slug);
    if (!article) throw new HttpErrorResponse({ status: 404, url: `/api/Help/${slug}` });
    return article;
  }
}

describe('Help', () => {
  let api: HelpApiServiceMock;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([{ path: 'help', children: helpRoutes }], withComponentInputBinding()),
        { provide: HelpApiService, useClass: HelpApiServiceMock },
        { provide: HelpApiServiceMock, useExisting: HelpApiService }
      ]
    });
    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(HelpApiServiceMock);
  });

  async function open(url: string): Promise<{ harness: RouterTestingHarness; el: () => HTMLElement }> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    await harness.fixture.whenStable();
    return { harness, el: () => harness.routeNativeElement as HTMLElement };
  }

  it('should list articles with links to them', async () => {
    const { el } = await open('/help');

    const links = el().querySelectorAll('#help-articles a');
    expect(links.length).toBe(2);
    expect(links[0].textContent).toBe('Пространство и участники');
    expect(links[0].getAttribute('href')).toBe('/help/workspace');
  });

  it('should offer a retry when the list did not load', async () => {
    api.failList = true;
    const { harness, el } = await open('/help');
    expect(el().querySelector('#help-error')?.textContent).toContain('Не удалось загрузить справку');

    api.failList = false;
    (el().querySelector('#help-error button') as HTMLButtonElement).click();
    await harness.fixture.whenStable();

    expect(el().querySelectorAll('#help-articles a').length).toBe(2);
  });

  it('should render the article as markdown without the search phrases', async () => {
    const { el } = await open('/help/workspace');

    expect(el().querySelector('#help-title')?.textContent).toBe('Пространство и участники');
    const body = el().querySelector('#help-body') as HTMLElement;
    expect(body.querySelector('h2')?.textContent).toBe('Как пригласить человека');
    expect(body.querySelector('a')?.getAttribute('href')).toBe('/workspace/members');
    expect(body.querySelector('strong')?.textContent).toBe('Пригласить');
    expect(body.querySelectorAll('li').length).toBe(2);
    expect(body.textContent).not.toContain('Как спрашивают');
    expect(body.textContent).not.toContain('page:');
  });

  it('should switch to another article on the same screen', async () => {
    const { harness, el } = await open('/help/workspace');

    await harness.navigateByUrl('/help/account');
    await harness.fixture.whenStable();

    expect(el().querySelector('#help-title')?.textContent).toBe('Аккаунт');
  });

  it('should say so when the article does not exist', async () => {
    const { el } = await open('/help/missing');

    expect(el().querySelector('#help-not-found')?.textContent).toContain('Такой статьи нет');
    expect(el().querySelector('#help-back')?.getAttribute('href')).toBe('/help');
  });

  it('should show the screen in English after switching the language', async () => {
    await TestBed.inject(I18nService).setLang('en');
    const { el } = await open('/help');

    expect(el().querySelector('h1')?.textContent).toBe('Help');
  });
});

describe('readerMarkdown', () => {
  it('should drop front matter and search phrases, keeping the text', () => {
    expect(readerMarkdown('---\ntitle: X\n---\n## A\n\nText\n\nКак спрашивают: a; b\n')).toBe('## A\n\nText');
  });
});
