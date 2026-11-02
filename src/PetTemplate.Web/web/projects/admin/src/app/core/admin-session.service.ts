import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AdminRole } from './admin-role';
import { Admin, AdminLoginResult } from './admin.model';

const TOKEN_KEY = 'adminToken';
const EXPIRES_KEY = 'adminTokenExpires';

/**
 * Сессия админки (спец. admin, 3.3): токен своей схемы и его срок.
 *
 * Хранилище — `localStorage` поддомена: продукту на `example.com` оно физически
 * недоступно, ради этого админка и вынесена отдельно.
 *
 * Токен живёт час и не продлевается: по истечении срока сессия заканчивается сама
 * и уводит на вход, раньше — кнопкой «Выйти» в шапке. Узнать об этом по ответу сервера
 * нельзя — просроченный токен там получает тот же `404`, что и чужой, — поэтому
 * срок отсчитывает клиент
 */
@Injectable({ providedIn: 'root' })
export class AdminSessionService {
  private readonly router = inject(Router);
  private timer: ReturnType<typeof setTimeout> | undefined;

  /** Кто вошёл. `null` до входа и до ответа `Admin/Me` после перезагрузки */
  readonly admin = signal<Admin | null>(null);

  get token(): string | null {
    return this.isActive() ? localStorage.getItem(TOKEN_KEY) : null;
  }

  get expiresAt(): Date | null {
    const value = localStorage.getItem(EXPIRES_KEY);
    return value ? new Date(value) : null;
  }

  isActive(): boolean {
    const expires = this.expiresAt;
    return !!localStorage.getItem(TOKEN_KEY) && !!expires && expires.getTime() > Date.now();
  }

  start(result: AdminLoginResult): void {
    localStorage.setItem(TOKEN_KEY, result.token);
    localStorage.setItem(EXPIRES_KEY, result.expiresUtc);
    this.admin.set(result.admin);
    this.scheduleEnd();
  }

  /** После перезагрузки: токен ещё жив — снова ставим будильник на его срок */
  resume(admin: Admin): void {
    this.admin.set(admin);
    this.scheduleEnd();
  }

  end(): void {
    clearTimeout(this.timer);
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(EXPIRES_KEY);
    this.admin.set(null);
    void this.router.navigateByUrl('/login');
  }

  /** Роль из токена. Без роли в аргументе — «любая»: вошёл, значит админ */
  hasRole(role: AdminRole | null): boolean {
    const roles = this.admin()?.roles ?? [];
    return role === null ? roles.length > 0 : roles.includes(role);
  }

  private scheduleEnd(): void {
    clearTimeout(this.timer);
    const left = (this.expiresAt?.getTime() ?? 0) - Date.now();
    this.timer = setTimeout(() => this.end(), Math.max(left, 0));
  }
}
