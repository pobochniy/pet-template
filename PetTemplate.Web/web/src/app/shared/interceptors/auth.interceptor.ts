import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthApiService } from '../api/auth-api.service';
import { Router } from '@angular/router';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthApiService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        // Попытка обновить токен
        authService.refreshToken().then(
          (newToken) => {
            if (newToken) {
              // Токен обновлен, можно повторить запрос
              // Но для простоты просто перезагружаем страницу
              window.location.reload();
            } else {
              router.navigate(['/auth/login']);
            }
          },
          () => {
            router.navigate(['/auth/login']);
          }
        );
      }
      return throwError(() => error);
    })
  );
};
