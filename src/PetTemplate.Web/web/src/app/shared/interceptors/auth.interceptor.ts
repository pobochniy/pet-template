import { Injector, inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { UserService } from '../core/user.service';
import { TokenService } from '../api/token.service';
import { AlertsService } from '../alerts/alerts.service';
import { injectI18n } from '../i18n/inject-i18n';

/** Адрес «моих прав» — его отказ перечитывание прав не запускает (иначе круг) */
const MY_PERMISSIONS_URL = '/api/Workspace/MyPermissions';

/**
 * JWT в каждый запрос к своему API и выход по 401: сервер ответил 401 — держать пользователя «внутри»
 * уже не на чем, а без чистки меню и профиль остаются от прежнего входа.
 * Обновления токена здесь нет намеренно: refresh-токенов в системе нет (спец. authorization, 2.1)
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // Чужой домен нашей сессии не касается: токен туда не отправляем и его 401 нас не выкидывает
  if (!req.url.startsWith('/api/')) return next(req);

  const userService = inject(UserService);
  const injector = inject(Injector);
  const alerts = inject(AlertsService);
  const router = inject(Router);
  const i18n = injectI18n();

  const token = inject(TokenService).Token;
  const request = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((error: unknown) => {
      // isAuthenticated — заодно защита от лавины: страница шлёт несколько запросов, и все они отвечают 401
      if (error instanceof HttpErrorResponse && error.status === 401 && userService.isAuthenticated) {
        // Отказ — тому токену, что ушёл в запросе. Соседняя вкладка могла уже записать
        // свежий: тогда сессия не стирается, а догоняет хранилище, и выхода нет
        void userService.expireSession(token);

        if (!userService.isAuthenticated) {
          alerts.push('danger', i18n.t('core.error.sessionExpired'), 5000);
          void router.navigateByUrl('/auth/login');
        }
      }

      // 403 своего API — права, вероятно, поменяли чужими руками: перечитать набор,
      // чтобы меню и гарды догнали сервер (спец. authorization, 2.2)
      if (error instanceof HttpErrorResponse && error.status === 403 && !req.url.startsWith(MY_PERMISSIONS_URL)) {
        // `import()`: сервис перечитывания — своим чанком, в initial только повод
        // Ушли со страницы (уничтожен инжектор), пока грузился чанк, — перечитывать некому
        void import('../core/permissions-refresh.service')
          .then(m => injector.get(m.PermissionsRefreshService).refresh('forbidden'))
          .catch(() => undefined);
      }

      return throwError(() => error);
    })
  );
};
