import { Injectable, signal } from '@angular/core';
import { safeStorage } from './safe-storage';

/** Ключ активного пространства — в `sessionStorage` вкладки и в `localStorage` как умолчание (спец. authorization, 1.2) */
export const CURRENT_WORKSPACE_KEY = 'currentWorkspaceId';

/**
 * Активное пространство: его id уходит заголовком `X-Workspace-Id` в каждом запросе
 * (`workspaceInterceptor`) и переживает перезагрузку страницы.
 *
 * Своё у каждой вкладки: в одной открыто одно пространство, в соседней — другое,
 * и переключение в одной не уводит запросы другой. Поэтому id лежит в `sessionStorage`
 * вкладки; `localStorage` хранит последний выбор — с него начинает новая вкладка.
 *
 * Только хранение. Переключать пространство надо вместе с правами — это делает
 * `UserService.switchWorkspace`; поставить id в обход значило бы слать запросы
 * в одно пространство, а меню рисовать по правам другого
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceContextService {
  private readonly _currentWorkspaceId = signal<string | null>(readStored());

  readonly currentWorkspaceId = this._currentWorkspaceId.asReadonly();

  getCurrentWorkspaceId(): string | null {
    return this._currentWorkspaceId();
  }

  setCurrentWorkspaceId(id: string | null): void {
    if (id != null) {
      safeStorage.set(CURRENT_WORKSPACE_KEY, id, 'session');
      safeStorage.set(CURRENT_WORKSPACE_KEY, id);
    } else {
      safeStorage.remove(CURRENT_WORKSPACE_KEY, 'session');
      safeStorage.remove(CURRENT_WORKSPACE_KEY);
    }

    this._currentWorkspaceId.set(id);
  }

  clearCurrentWorkspace(): void {
    this.setCurrentWorkspaceId(null);
  }
}

function readStored(): string | null {
  return safeStorage.get(CURRENT_WORKSPACE_KEY, 'session') ?? safeStorage.get(CURRENT_WORKSPACE_KEY);
}
