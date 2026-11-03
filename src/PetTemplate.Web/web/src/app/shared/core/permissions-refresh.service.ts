import { DestroyRef, Injectable, Injector, inject } from '@angular/core';
import { UserService } from './user.service';

/** Возврат во вкладку — не чаще раза в минуту: переключение окон не должно засыпать сервер */
export const FOCUS_REFRESH_INTERVAL_MS = 60_000;

/** `403` — не чаще раза в 5 секунд: страница шлёт несколько запросов, и отказать могут все */
export const FORBIDDEN_REFRESH_INTERVAL_MS = 5_000;

export type PermissionsRefreshReason = 'focus' | 'forbidden';

/**
 * Права, поменянные чужими руками (спец. authorization, 2.2): выдача и отзыв прав,
 * удаление участника. Сервер проверяет права по базе на каждом запросе, а меню, кнопки
 * и гарды клиента — по набору из ответа входа. Поэтому набор перечитывается
 * (`GET api/Workspace/MyPermissions`, без перевыпуска токена):
 *
 * - на `403` своего API — сервер уже знает то, чего не знает клиент (`authInterceptor`);
 * - на возврат во вкладку (`focus`, `visibilitychange`) — так видна и выдача права,
 *   на которую `403` не бывает.
 *
 * Свежий набор раскладывает `UserService.applyPermissions`: меню перерисовывается
 * от сигналов само, открытый экран проходит гарды заново (`recheckRouteAccess`).
 * Запрос один на раз: пока идёт — новые поводы ждут его.
 *
 * `WorkspaceApiService` — через `import()`, как у шапки: в initial только повод и порог
 */
@Injectable({ providedIn: 'root' })
export class PermissionsRefreshService {
  private readonly user = inject(UserService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private inFlight: Promise<void> | null = null;
  private lastAt = Number.NEGATIVE_INFINITY;

  /** Слушать возврат во вкладку — один раз, из корневого компонента */
  watch(): void {
    if (typeof window === 'undefined') return;

    const onFocus = () => void this.refresh('focus');
    const onVisible = () => {
      if (document.visibilityState === 'visible') void this.refresh('focus');
    };

    window.addEventListener('focus', onFocus);
    document.addEventListener('visibilitychange', onVisible);
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('focus', onFocus);
      document.removeEventListener('visibilitychange', onVisible);
    });
  }

  /** Перечитать права, если повод не слишком частый. Ошибки не бросает: это фон */
  refresh(reason: PermissionsRefreshReason): Promise<void> {
    if (!this.user.isAuthenticated) return Promise.resolve();
    if (this.inFlight) return this.inFlight;

    const interval = reason === 'forbidden' ? FORBIDDEN_REFRESH_INTERVAL_MS : FOCUS_REFRESH_INTERVAL_MS;
    if (Date.now() - this.lastAt < interval) return Promise.resolve();

    this.lastAt = Date.now();
    this.inFlight = this.load().finally(() => (this.inFlight = null));
    return this.inFlight;
  }

  private async load(): Promise<void> {
    const userId = this.user.profile()?.id;
    if (!userId) return;

    try {
      const { WorkspaceApiService } = await import('../api/workspace-api.service');
      const permissions = await this.injector.get(WorkspaceApiService).myPermissions();
      this.user.applyPermissions(permissions, userId);
    } catch {
      // 401 уже закончил сессию в `authInterceptor`; сеть и 5xx — подождём следующего повода
    }
  }
}
