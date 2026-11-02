import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { UserService } from '../core/user.service';
import { TokenService } from '../api/token.service';
import { AlertsService } from '../alerts/alerts.service';
import { I18nService } from '../i18n/i18n.service';

/**
 * JWT в каждый запрос к своему API и выход по 401: сервер ответил 401 — держать пользователя «внутри»
 * уже не на чем, а без чистки меню и профиль остаются от прежнего входа.
 * Обновления токена здесь нет намеренно: refresh-токенов в системе нет (спец. authorization, 2.1)
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // Чужой домен нашей сессии не касается: токен туда не отправляем и его 401 нас не выкидывает
  if (!req.url.startsWith('/api/')) return next(req);

  const userService = inject(UserService);
  const alerts = inject(AlertsService);
  const router = inject(Router);
  const i18n = inject(I18nService);

  const token = inject(TokenService).Token;
  const request = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((error: unknown) => {
      // isAuthenticated — заодно защита от лавины: страница шлёт несколько запросов, и все они отвечают 401
      if (error instanceof HttpErrorResponse && error.status === 401 && userService.isAuthenticated) {
        void userService.expireSession();
        alerts.push('danger', i18n.t('core.error.sessionExpired'), 5000);
        void router.navigateByUrl('/auth/login');
      }

      return throwError(() => error);
    })
  );
};
