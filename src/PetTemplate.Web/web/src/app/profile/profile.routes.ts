import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { I18nService } from '../shared/i18n/i18n.service';

export const profileRoutes: Routes = [
  { path: 'edit', loadComponent: () => import('./edit/edit.component').then(m => m.EditComponent) },
  { path: 'avatar', loadComponent: () => import('./avatar/avatar').then(m => m.Avatar) },
  // Слияние и удаление аккаунта (спец. accounts, 7 и 9) — свои ленивые чанки: в профиль их не тянем.
  // Гостя на вход уводят сами экраны, а не `signedInGuard`: его импорт отсюда выносит гард
  // из `main` отдельным общим чанком, и initial растёт на пустом месте (tech-debt/bundle.md)
  { path: 'merge', loadComponent: () => import('./merge/merge.component').then(m => m.MergeComponent) },
  { path: 'delete', loadComponent: () => import('./delete/delete-account.component').then(m => m.DeleteAccountComponent) },
  {
    // Память ассистента (спец. assistant, 12.5) — в профиле, рядом с личными настройками.
    // Словарь — `assistant`: и подписи, и коды отказов приезжают из него, вместе с чанком
    path: 'assistant-memory',
    loadComponent: () => Promise.all([
      import('./assistant-memory/assistant-memory.component'),
      inject(I18nService).loadScope('assistant')
    ]).then(([m]) => m.AssistantMemoryComponent)
  },
  { path: 'view/:userId', loadComponent: () => import('./view/view.component').then(m => m.ViewComponent) },
  { path: '', redirectTo: 'edit', pathMatch: 'full' }
];
