import { Routes } from '@angular/router';

export const authRoutes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login.component').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./register/register.component').then(m => m.RegisterComponent) },
  // Восстановление доступа (спец. accounts, 8.2): каждый экран — свой ленивый чанк
  { path: 'recover', loadComponent: () => import('./recover/recover.component').then(m => m.RecoverComponent) },
  { path: 'reset/:token', loadComponent: () => import('./reset/reset.component').then(m => m.ResetComponent) },
  {
    path: 'confirm-email/:token',
    loadComponent: () => import('./confirm-email/confirm-email.component').then(m => m.ConfirmEmailComponent)
  },
  // Возврат с площадки (спец. accounts, 5.4): один экран, площадку называет маршрут
  {
    path: 'telegram',
    loadComponent: () => import('./external-callback/external-callback.component').then(m => m.ExternalCallbackComponent),
    data: { provider: 'telegram' }
  },
  {
    path: 'vk',
    loadComponent: () => import('./external-callback/external-callback.component').then(m => m.ExternalCallbackComponent),
    data: { provider: 'vk' }
  }
];
