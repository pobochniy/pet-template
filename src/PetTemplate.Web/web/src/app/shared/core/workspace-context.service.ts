import { Injectable, signal } from '@angular/core';

/** Ключ активного пространства в `localStorage` (спец. authorization, 1.2) */
export const CURRENT_WORKSPACE_KEY = 'currentWorkspaceId';

/**
 * Активное пространство: его id уходит заголовком `X-Workspace-Id` в каждом запросе
 * (`workspaceInterceptor`) и переживает перезагрузку страницы.
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
    try {
      if (id != null) localStorage.setItem(CURRENT_WORKSPACE_KEY, id);
      else localStorage.removeItem(CURRENT_WORKSPACE_KEY);
    } catch {
      // Хранилище закрыто (приватный режим) — пространство живёт до перезагрузки
    }

    this._currentWorkspaceId.set(id);
  }

  clearCurrentWorkspace(): void {
    this.setCurrentWorkspaceId(null);
  }
}

function readStored(): string | null {
  try {
    return localStorage.getItem(CURRENT_WORKSPACE_KEY);
  } catch {
    return null;
  }
}
