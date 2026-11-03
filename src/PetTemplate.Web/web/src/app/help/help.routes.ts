import { Routes } from '@angular/router';

/**
 * Справка (спец. help, 6.5): список статей и статья по `slug`. Без гардов — справку
 * читают и до входа. Словарь `help` грузит `app.routes.ts` вместе с чанком раздела
 */
export const helpRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./help-list/help-list.component').then(m => m.HelpListComponent)
  },
  {
    path: ':slug',
    loadComponent: () => import('./help-article/help-article.component').then(m => m.HelpArticleComponent)
  }
];
