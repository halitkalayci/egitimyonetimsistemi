import { Routes } from '@angular/router';

import { authGuard, mustChangePasswordGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'giris',
    loadComponent: () => import('./features/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'sifre-degistir',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/password-change/password-change.page').then((m) => m.PasswordChangePage),
  },
  {
    path: 'kullanicilar',
    canActivate: [authGuard, mustChangePasswordGuard],
    loadComponent: () => import('./features/users/users.page').then((m) => m.UsersPage),
  },
  {
    path: '',
    canActivate: [authGuard, mustChangePasswordGuard],
    loadComponent: () =>
      import('./features/system-info/system-info.page').then((m) => m.SystemInfoPage),
  },
  { path: '**', redirectTo: '' },
];
