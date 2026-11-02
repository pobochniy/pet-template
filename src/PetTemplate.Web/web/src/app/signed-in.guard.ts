import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { UserService } from './shared/core/user.service';

/**
 * Пускает только вошедшего. Гостя уводит на вход: личные страницы (настройки
 * пространств) без входа показывали бы пустую форму, а их запросы — 401.
 *
 * Это барьер интерфейса, а не безопасности: данные без входа не отдаёт сервер
 */
export const signedInGuard: CanActivateFn = () => {
  if (inject(UserService).isAuthenticated) return true;

  return inject(Router).parseUrl('/auth/login');
};
