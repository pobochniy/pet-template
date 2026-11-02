import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { AdminApiService } from './admin-api.service';
import { AdminSessionService } from './admin-session.service';
import { Section } from '../shell/sections';
import { apiErrorText } from './api-error';

/** Состояние навигации на вход, когда `Admin/Me` не ответил, — читает `LoginComponent` */
export interface AdminUnavailableState {
  adminUnavailable: string;
}

/**
 * Пускает в оболочку только с живым токеном. После перезагрузки страницы, кто
 * вошёл, спрашивает у `Admin/Me`: отказ там (`401`/`404`) значит, что токен уже
 * не годится, и сессия кончается.
 *
 * Сеть или `5xx` о токене не говорят ничего — сессия остаётся, а экран входа
 * говорит, что сервер не ответил, и предлагает повторить. Иначе мигнувший API
 * выкидывал бы из админки посреди часа, на который выдан токен
 */
export const adminGuard: CanActivateFn = async () => {
  const session = inject(AdminSessionService);
  const router = inject(Router);
  const api = inject(AdminApiService);

  if (!session.isActive()) return router.parseUrl('/login');
  if (session.admin()) return true;

  try {
    session.resume(await api.me());
    return true;
  } catch (error) {
    if (error instanceof HttpErrorResponse
      && (error.status === HttpStatusCode.Unauthorized || error.status === HttpStatusCode.NotFound)) {
      session.end();
      return false;
    }

    const state: AdminUnavailableState = { adminUnavailable: apiErrorText(error) };
    void router.navigateByUrl('/login', { state });
    return false;
  }
};

/**
 * Раздел без нужной роли не открывается и по прямой ссылке. Проверка удобства,
 * а не защиты: данные раздела сервер без роли всё равно не отдаст
 */
export const roleGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const section = route.data['section'] as Section;
  return inject(AdminSessionService).hasRole(section.role) || inject(Router).parseUrl('/');
};
