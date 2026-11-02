import { Type } from '@angular/core';
import { Routes } from '@angular/router';
import { adminGuard, roleGuard } from './core/admin.guard';
import { LoginComponent } from './login/login.component';
import { ShellComponent } from './shell/shell.component';
import { SectionComponent } from './shell/section.component';
import { SECTIONS } from './shell/sections';
import { InviteComponent } from './invite/invite.component';

/**
 * Экраны разделов, которые уже есть; остальные пока открываются заголовком. Каждый —
 * своим чанком: вкладку открывает не всякий админ, а начальная сборка держит бюджет
 */
const SCREENS: Partial<Record<string, () => Promise<Type<unknown>>>> = {
  summary: () => import('./summary/summary.component').then(x => x.SummaryComponent),
  models: () => import('./models/models.component').then(x => x.ModelsComponent),
  dialogs: () => import('./dialogs/dialogs.component').then(x => x.DialogsComponent),
  accounts: () => import('./accounts/accounts.component').then(x => x.AccountsComponent),
  'account-journal': () => import('./account-journal/account-journal.component').then(x => x.AccountJournalComponent),
  billing: () => import('./billing/billing.component').then(x => x.BillingComponent),
  admins: () => import('./admins/admins.component').then(x => x.AdminsComponent),
  'access-log': () => import('./access-log/access-log.component').then(x => x.AccessLogComponent)
};

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  // Вне оболочки: у пришедшего по ссылке ни ролей, ни сессии (спец. admin, 3.4)
  { path: 'invite/:token', component: InviteComponent },
  {
    path: '',
    component: ShellComponent,
    canActivate: [adminGuard],
    children: [
      ...SECTIONS.map(section => ({
        path: section.path,
        loadComponent: SCREENS[section.path] ?? (() => SectionComponent),
        canActivate: [roleGuard],
        data: { section }
      })),
      { path: '', pathMatch: 'full' as const, redirectTo: SECTIONS[0].path }
    ]
  },
  { path: '**', redirectTo: '' }
];
