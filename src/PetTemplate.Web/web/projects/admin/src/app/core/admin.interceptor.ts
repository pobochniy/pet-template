import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AdminSessionService } from './admin-session.service';

/**
 * Токен админской схемы на запросы к своему API. Без `X-Workspace-Id`: у админских
 * запросов workspace нет (спец. admin, 3.5)
 */
export const adminInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AdminSessionService).token;

  if (!token || !req.url.startsWith('/api/')) return next(req);

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
