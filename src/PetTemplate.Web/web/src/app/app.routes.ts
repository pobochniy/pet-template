import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { signedInGuard } from './signed-in.guard';
import { I18nService } from './shared/i18n/i18n.service';
import { LazyScope } from './shared/i18n/generated/i18n-keys';
import { ASSISTANT_SCOPES } from './assistant/assistant.scopes';
import { AUTH_SCOPES } from './auth/auth.scopes';
import { HELP_SCOPES } from './help/help.scopes';
import { HOME_SCOPES } from './home/home.scopes';
import { INVITE_SCOPES } from './invite/invite.scopes';
import { MINIAPP_SCOPES } from './miniapp/miniapp.scopes';
import { PROFILE_SCOPES } from './profile/profile.scopes';
import { WORKSPACE_SCOPES } from './workspace/workspace.scopes';

/**
 * Каждый раздел — свой ленивый чанк. В initial остаются только оболочка
 * (меню, шапка, плашки) и то, что ей нужно.
 *
 * Словари раздела — константа `*_SCOPES` из `{раздел}/{раздел}.scopes.ts`, та же, что
 * у `injectI18n(...)` его экранов (свой словарь и чужие, чьи коды раздел показывает), —
 * грузятся **рядом** с его чанком, в одном `Promise.all`, а не резолвером: в конвейере роутера фаза `RESOLVE` идёт до загрузки
 * компонентов, и резолвер тянул бы словарь **перед** чанком, а не вместе
 * с ним. `loadChildren`/`loadComponent` выполняются в контексте инжектора,
 * поэтому `inject` здесь законен.
 */
function withScope<T>(scopes: readonly LazyScope[], chunk: Promise<T>): Promise<T> {
  const i18n = inject(I18nService);
  return Promise.all([chunk, ...scopes.map(s => i18n.loadScope(s))]).then(([m]) => m);
}

export const routes: Routes = [
  {
    path: '',
    // `assistant` — строка ассистента на главной (спец. assistant, 2) и плашка
    // исчерпанного лимита пространства на её месте (13.6)
    loadComponent: () => withScope(HOME_SCOPES, import('./home/home.component')).then(m => m.HomeComponent)
  },
  {
    // Чат с ассистентом (спец. assistant, 2): отдельный экран, а не окно — к разговору
    // возвращаются. Тред — в пространстве, поэтому только вошедшему
    path: 'assistant',
    loadComponent: () => withScope(ASSISTANT_SCOPES, import('./assistant/assistant.component')).then(m => m.AssistantComponent),
    canActivate: [signedInGuard],
    runGuardsAndResolvers: 'always'
  },
  {
    // Mini-app Telegram и VK (спец. accounts, 5.6): один экран, площадку называет маршрут.
    // `auth` — отказы развилки приезжают кодами `auth.error.*`
    path: 'miniapp',
    data: { provider: 'telegram' },
    loadComponent: () => withScope(MINIAPP_SCOPES, import('./miniapp/miniapp.component')).then(m => m.MiniappComponent)
  },
  {
    path: 'vk',
    data: { provider: 'vk' },
    loadComponent: () => withScope(MINIAPP_SCOPES, import('./miniapp/miniapp.component')).then(m => m.MiniappComponent)
  },
  {
    // «Добро пожаловать» (спец. accounts, 5.4) — первый экран аккаунта, заведённого
    // через Telegram или VK; адрес из спеки, поэтому вне `auth/`
    path: 'welcome',
    loadComponent: () => withScope(AUTH_SCOPES, import('./auth/welcome/welcome.component')).then(m => m.WelcomeComponent),
    canActivate: [signedInGuard],
    runGuardsAndResolvers: 'always'
  },
  {
    path: 'auth',
    loadChildren: () => withScope(AUTH_SCOPES, import('./auth/auth.routes')).then(m => m.authRoutes)
  },
  {
    path: 'profile',
    // `auth` — для отказов своего аккаунта: «логин занят», «почта занята», «неверный
    // текущий пароль» приезжают кодами `auth.error.*` (спец. accounts, 6)
    loadChildren: () => withScope(PROFILE_SCOPES, import('./profile/profile.routes')).then(m => m.profileRoutes)
  },
  {
    // Настройки пространств и участники (спец. authorization, 7). Второй словарь —
    // `invite`: выдача и отзыв приглашения отвечают кодами его скоупа
    path: 'workspace',
    loadChildren: () => withScope(WORKSPACE_SCOPES, import('./workspace/workspace.routes'))
      .then(m => m.workspaceRoutes),
    canActivate: [signedInGuard],
    runGuardsAndResolvers: 'always'
  },
  {
    // Лендинг приглашения — анонимный (спец. authorization, 6.3)
    path: 'invite/:token',
    // `auth` — регистрация по ссылке отказывает теми же кодами, что обычная:
    // «логин занят» — `auth.error.userNameTaken` (спец. accounts, 4.1)
    loadComponent: () => withScope(INVITE_SCOPES, import('./invite/invite.component')).then(m => m.InviteComponent)
  },
  {
    // Справка на сайте (спец. help, 6.5): статьи `help/` из API, тем же отрисовщиком
    // markdown, что ответы ассистента. Без входа — справку читают и до регистрации
    path: 'help',
    loadChildren: () => withScope(HELP_SCOPES, import('./help/help.routes')).then(m => m.helpRoutes)
  },
  // Экран прав шаблона вырос в страницу участников пространства (спец. authorization, 7.3)
  { path: 'rolemanagement', redirectTo: 'workspace/members' }
];
