import { HttpErrorResponse } from '@angular/common/http';

/**
 * Отказ API как есть (спец. admin, 3.5): кодов админка не заводит и не переводит —
 * текст из тела, если он там есть, иначе статус
 */
export function apiErrorText(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return String(error);

  const body = error.error as { error?: unknown } | string | null;
  if (typeof body === 'string' && body) return body;
  if (body && typeof body === 'object' && typeof body.error === 'string') return body.error;

  return `${error.status} ${error.statusText}`.trim();
}
