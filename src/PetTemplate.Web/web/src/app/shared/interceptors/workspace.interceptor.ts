import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { WorkspaceContextService } from '../core/workspace-context.service';

/** Заголовок активного пространства — `WorkspaceConstants.Header` на сервере */
export const WORKSPACE_HEADER = 'X-Workspace-Id';

/**
 * Активное пространство в каждый запрос к своему API (спец. authorization, 1.2).
 *
 * Чужому хосту заголовок не уходит: в нём id пространства. Запрос, который уже несёт
 * заголовок сам, не трогаем — так правится не активное пространство (название в строке
 * списка своих, раздел 7.2): право проверяется у того, что назван в заголовке
 */
export const workspaceInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/') || req.headers.has(WORKSPACE_HEADER)) return next(req);

  const workspaceId = inject(WorkspaceContextService).getCurrentWorkspaceId();
  if (workspaceId == null) return next(req);

  return next(req.clone({ setHeaders: { [WORKSPACE_HEADER]: workspaceId } }));
};
