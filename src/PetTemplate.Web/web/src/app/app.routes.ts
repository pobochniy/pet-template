import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { signedInGuard } from './signed-in.guard';
import { I18nService } from './shared/i18n/i18n.service';
import { LazyScope } from './shared/i18n/generated/i18n-keys';

/**
 * Каждый раздел — свой ленивый чанк. В initial остаются только оболочка
 * (меню, шапка, плашки) и то, что ей нужно.
 *
 * Словарь раздела (и чужие словари, чьи коды раздел показывает — `more`)
 * грузится **рядом** с его чанком, в одном `Promise.all`, а не резолвером: в конвейере роутера фаза `RESOLVE` идёт до загрузки
 * компонентов, и резолвер тянул бы словарь **перед** чанком, а не вместе
 * с ним. `loadChildren`/`loadComponent` выполняются в контексте инжектора,
 * поэтому `inject` здесь законен.
 */
function withScope<T>(scope: LazyScope, chunk: Promise<T>, ...more: LazyScope[]): Promise<T> {
  const i18n = inject(I18nService);
  return Promise.all([chunk, ...[scope, ...more].map(s => i18n.loadScope(s))]).then(([m]) => m);
}

export const routes: Routes = [
  {
    path: '',
    // `assistant` — строка ассистента на главной (спец. assistant, 2) и плашка
    // исчерпанного лимита пространства на её месте (13.6)
    loadComponent: () => withScope('home', import('./home/home.component'), 'assistant').then(m => m.HomeComponent)
  },
  {
    // Чат с ассистентом (спец. assistant, 2): отдельный экран, а не окно — к разговору
    // возвращаются. Тред — в пространстве, поэтому только вошедшему
    path: 'assistant',
    loadComponent: () => withScope('assistant', import('./assistant/assistant.component')).then(m => m.AssistantComponent),
    canActivate: [signedInGuard]
  },
  {
    // Mini-app Telegram и VK (спец. accounts, 5.6): один экран, площадку называет маршрут.
    // `auth` — отказы развилки приезжают кодами `auth.error.*`
    path: 'miniapp',
    data: { provider: 'telegram' },
    loadComponent: () => withScope('miniapp', import('./miniapp/miniapp.component'), 'auth').then(m => m.MiniappComponent)
  },
  {
    path: 'vk',
    data: { provider: 'vk' },
    loadComponent: () => withScope('miniapp', import('./miniapp/miniapp.component'), 'auth').then(m => m.MiniappComponent)
  },
  {
    // «Добро пожаловать» (спец. accounts, 5.4) — первый экран аккаунта, заведённого
    // через Telegram или VK; адрес из спеки, поэтому вне `auth/`
    path: 'welcome',
    loadComponent: () => withScope('auth', import('./auth/welcome/welcome.component')).then(m => m.WelcomeComponent),
    canActivate: [signedInGuard]
  },
  {
    path: 'auth',
    loadChildren: () => withScope('auth', import('./auth/auth.routes')).then(m => m.authRoutes)
  },
  {
    path: 'profile',
    // `auth` — для отказов своего аккаунта: «логин занят», «почта занята», «неверный
    // текущий пароль» приезжают кодами `auth.error.*` (спец. accounts, 6)
    loadChildren: () => withScope('profile', import('./profile/profile.routes'), 'auth').then(m => m.profileRoutes)
  },
  {
    // Настройки пространств и участники (спец. authorization, 7). Второй словарь —
    // `invite`: выдача и отзыв приглашения отвечают кодами его скоупа
    path: 'workspace',
    loadChildren: () => withScope('workspace', import('./workspace/workspace.routes'), 'invite')
      .then(m => m.workspaceRoutes),
    canActivate: [signedInGuard]
  },
  {
    // Лендинг приглашения — анонимный (спец. authorization, 6.3)
    path: 'invite/:token',
    // `auth` — регистрация по ссылке отказывает теми же кодами, что обычная:
    // «логин занят» — `auth.error.userNameTaken` (спец. accounts, 4.1)
    loadComponent: () => withScope('invite', import('./invite/invite.component'), 'auth').then(m => m.InviteComponent)
  },
  // Экран прав шаблона вырос в страницу участников пространства (спец. authorization, 7.3)
  { path: 'rolemanagement', redirectTo: 'workspace/members' }
];
