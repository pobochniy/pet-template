import { Routes } from '@angular/router';
// walletConnectedGuard is ready at shared/guards/wallet-connected.guard.ts
// Apply it to wager game routes when feature 009-ton-wager-game is implemented

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./home/home.component').then(m => m.HomeComponent)
  },
  {
    path: 'miniapp',
    loadComponent: () => import('./miniapp/miniapp.component').then(m => m.MiniappComponent)
  },
  {
    path: 'profile',
    loadChildren: () => import('./profile/profile.routes').then(m => m.PROFILE_ROUTES)
  },
  {
    path: 'auth/login',
    loadComponent: () => import('./auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'auth/register',
    loadComponent: () => import('./auth/register/register.component').then(m => m.RegisterComponent)
  },
];
