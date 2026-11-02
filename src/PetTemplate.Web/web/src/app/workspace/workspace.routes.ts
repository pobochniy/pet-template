import { Routes } from '@angular/router';
import { rolesGuard } from '../roles.guard';
import { MEMBERS_PAGE_PERMISSIONS } from './workspace-permissions';

/**
 * Пространства: настройки (список своих, создание) и участники (спец. authorization, 7).
 * Словарь `workspace` грузит `app.routes.ts` вместе с чанком раздела; второй —
 * `invite`: выдача и отзыв приглашения отвечают кодами его скоупа
 * (`invite.error.cannotRevokeAccepted`), а `translateCode` ищет код в словаре
 * по первому сегменту.
 *
 * `settings` намеренно без guard'а: сюда приходят и те, у кого пространств ещё нет —
 * прав в несуществующем пространстве быть не может, а создать первое надо откуда-то
 */
export const workspaceRoutes: Routes = [
  { path: '', redirectTo: 'settings', pathMatch: 'full' },
  {
    path: 'settings',
    loadComponent: () => import('./settings/settings.component').then(m => m.WorkspaceSettingsComponent)
  },
  {
    path: 'members',
    loadComponent: () => import('./members/members.component').then(m => m.WorkspaceMembersComponent),
    canActivate: [rolesGuard],
    data: { allowedRoles: MEMBERS_PAGE_PERMISSIONS }
  }
];
