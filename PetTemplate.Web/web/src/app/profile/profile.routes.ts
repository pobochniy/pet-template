import { Routes } from '@angular/router';

export const PROFILE_ROUTES: Routes = [
  {
    path: 'edit',
    loadComponent: () => import('./edit/edit.component').then(m => m.EditComponent)
  },
  {
    path: 'avatar',
    loadComponent: () => import('./avatar/avatar').then(m => m.Avatar)
  },
  {
    path: 'view/:userId',
    loadComponent: () => import('./view/view.component').then(m => m.ViewComponent)
  },
  {
    path: 'wallet',
    loadComponent: () => import('./wallet/wallet.component').then(m => m.WalletComponent)
  },
  {
    path: '',
    redirectTo: 'edit',
    pathMatch: 'full'
  }
];
