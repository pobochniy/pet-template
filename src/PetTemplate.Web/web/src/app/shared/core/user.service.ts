import { Injectable, computed, inject, signal } from '@angular/core';
import { ProfileModel } from '../models/profile.model';
import { AuthenticationResultModel, PermissionsByWorkspace } from '../models/authentication-result.model';
import { PermissionEnum } from '../enums/permission.enum';
import { TokenService } from '../api/token.service';
import { PermissionsService } from '../services/permissions.service';
import { SessionResetOptions, SessionResetService } from './session-reset.service';
import { WorkspaceContextService } from './workspace-context.service';

/** Ключ пользователя сессии в `localStorage`: токен лежит отдельно (`TokenService`) */
export const SESSION_USER_KEY = 'user';

/** Пользователь сессии так, как он лежит в хранилище */
interface StoredUser {
  profile: ProfileModel;
  permissionsByWorkspace: PermissionsByWorkspace;
}

/**
 * Текущий вход: профиль и права по пространствам, а через `TokenService`,
 * `WorkspaceContextService` и `PermissionsService` — токен, активное пространство
 * и права в нём (спец. authorization, 2.1).
 *
 * Профиль и права — сигналы: шаблоны оболочки (`OnPush`, без Zone.js) перерисовываются
 * от их смены сами. Пользователь лежит в `localStorage` рядом с токеном — после
 * перезагрузки страницы сессия та же, пока жив токен
 */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly tokenService = inject(TokenService);
  private readonly permissionsService = inject(PermissionsService);
  private readonly workspaceContext = inject(WorkspaceContextService);
  private readonly sessionReset = inject(SessionResetService);

  private readonly stored = readStoredUser();

  private readonly _profile = signal<ProfileModel | null>(this.stored?.profile ?? null);
  private readonly _permissionsByWorkspace = signal<PermissionsByWorkspace>(this.stored?.permissionsByWorkspace ?? {});

  readonly profile = this._profile.asReadonly();
  readonly permissionsByWorkspace = this._permissionsByWorkspace.asReadonly();

  /** Свои пространства — ключи прав: членство и есть права (спец. authorization, 1) */
  readonly workspaceIds = computed(() => Object.keys(this._permissionsByWorkspace()));

  constructor() {
    // Права активного пространства — из сохранённого: после перезагрузки страницы
    // ни входа, ни переключения не было, и меню иначе осталось бы без пунктов
    this.ensureActiveWorkspace(this.workspaceContext.getCurrentWorkspaceId());
  }

  get isAuthenticated(): boolean {
    return this._profile() !== null;
  }

  setProfile(profile: ProfileModel | null): void {
    this._profile.set(profile);
    this.persist();
  }

  clearProfile(): void {
    this._profile.set(null);
    this.persist();
  }

  /**
   * Ответ входа и любого перевыпуска сессии (спец. authorization, 2.2) раскладывается
   * здесь одним кодом: токен, профиль, права по пространствам и активное пространство.
   *
   * Активным становится `activeWorkspaceId`, если он среди своих: созданное, принятое
   * по приглашению, оставшееся активным после выхода из другого. Иначе — основное,
   * а его нет — первое из доступных; пространств нет вовсе — активного тоже
   */
  applySession(result: AuthenticationResultModel, activeWorkspaceId: string | null = null): void {
    this.tokenService.Token = result.token;
    this._profile.set(result.profile);
    this._permissionsByWorkspace.set(result.permissionsByWorkspace ?? {});
    this.persist();

    this.ensureActiveWorkspace(activeWorkspaceId);
  }

  /**
   * Смена активного пространства. Пространство и права переключаются только вместе:
   * врозь они разъезжаются — запросы уходят с новым `X-Workspace-Id`, а разделы
   * остаются открытыми по правам прежнего. `null` — пространства нет вовсе
   */
  switchWorkspace(workspaceId: string | null): void {
    this.workspaceContext.setCurrentWorkspaceId(workspaceId);
    this.permissionsService.initPermissions(workspaceId ? this.permissionsIn(workspaceId) : []);
  }

  /**
   * Права в конкретном своём пространстве — не обязательно активном. В сессии они
   * разложены по пространствам, поэтому спрашивать сервер незачем: этим решается,
   * показывать ли строке списка кнопку в закрытый правом раздел
   */
  permissionsIn(workspaceId: string): PermissionEnum[] {
    return this._permissionsByWorkspace()[workspaceId] ?? [];
  }

  /**
   * Выход по кнопке. Сначала уборка состояния разделов (`SessionResetService`),
   * потом обнуление сессии: обработчику может понадобиться сервер,
   * а с пустым `Bearer` его запрос вернулся бы 401
   */
  async logOut(): Promise<void> {
    await this.sessionReset.run();
    this.clearSession();
  }

  /**
   * Вынужденный выход: сервер не принял токен (401). Зовёт `authInterceptor`.
   *
   * Порядок обратный `logOut()`: сначала обнуляем сессию, потом убираем разделы.
   * Ходить на сервер уже не с чем, а синхронное обнуление — то, на что смотрит
   * перехватчик (`isAuthenticated`), чтобы не показать плашку на каждый из нескольких
   * запросов страницы, ответивших 401 одновременно
   */
  async expireSession(): Promise<void> {
    this.clearSession();
    await this.sessionReset.run({ canPersist: false } satisfies SessionResetOptions);
  }

  /** Обнуление самого входа: токен, профиль, права, активное пространство */
  private clearSession(): void {
    this.tokenService.Token = '';
    this._profile.set(null);
    this._permissionsByWorkspace.set({});
    this.persist();
    this.switchWorkspace(null);
  }

  /**
   * Активное пространство обязано быть своим: из пространства, откуда человека убрали
   * или откуда он вышел, запросы не должны уходить с его заголовком
   */
  private ensureActiveWorkspace(preferred: string | null): void {
    const own = this.workspaceIds();
    const primary = this._profile()?.primaryWorkspaceId ?? null;

    const active = [preferred, primary, own[0] ?? null].find(id => id != null && own.includes(id)) ?? null;

    this.switchWorkspace(active);
  }

  private persist(): void {
    const profile = this._profile();

    try {
      if (profile == null) {
        localStorage.removeItem(SESSION_USER_KEY);
        return;
      }

      const user: StoredUser = { profile, permissionsByWorkspace: this._permissionsByWorkspace() };
      localStorage.setItem(SESSION_USER_KEY, JSON.stringify(user));
    } catch {
      // Хранилище закрыто — сессия живёт до перезагрузки
    }
  }
}

/** Битая или чужая запись не роняет приложение: просто входа нет */
function readStoredUser(): StoredUser | null {
  try {
    const raw = localStorage.getItem(SESSION_USER_KEY);
    if (!raw) return null;

    const user = JSON.parse(raw) as Partial<StoredUser>;

    return user?.profile ? { profile: user.profile, permissionsByWorkspace: user.permissionsByWorkspace ?? {} } : null;
  } catch {
    return null;
  }
}
