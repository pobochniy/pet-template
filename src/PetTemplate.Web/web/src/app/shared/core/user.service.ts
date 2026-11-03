import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { ProfileModel } from '../models/profile.model';
import { AuthenticationResultModel, PermissionsByWorkspace } from '../models/authentication-result.model';
import { PermissionEnum } from '../enums/permission.enum';
import { PermissionsService } from '../services/permissions.service';
import { SessionResetOptions, SessionResetService } from './session-reset.service';
import { WorkspaceContextService } from './workspace-context.service';
import { SESSION_KEY, StoredSession, readSession, removeSession, writeSession } from './session-store';

/**
 * Кто и где работает в этой вкладке: пользователь сессии и активное пространство.
 * От него зависят перечитывание данных экрана (`contextResource`) и проверки маршрута
 * (`SessionGuardsService`): сменилось одно из двух — данные и доступ уже про другое
 */
export interface SessionContext {
  readonly userId: string | null;
  readonly workspaceId: string | null;
}

/**
 * Текущий вход — единственное место, где сессия меняется (спец. authorization, 2.1):
 * токен, профиль и права по пространствам одной записью (`session-store.ts`), а через
 * `WorkspaceContextService` и `PermissionsService` — активное пространство и права в нём.
 * Api-сервисы сессию не трогают: они возвращают ответ сервера, раскладывает его этот фасад.
 *
 * Профиль и права — сигналы: шаблоны оболочки (`OnPush`, без Zone.js) перерисовываются
 * от их смены сами. Запись переживает перезагрузку и **общая у вкладок**: соседняя вкладка
 * может войти другим человеком, перевыпустить токен или выйти. Поэтому:
 *
 * - чужую запись вкладка не перезаписывает — только перечитывает (событие `storage`);
 * - 401 стирает запись, только если в ней всё ещё тот токен, который сервер не принял;
 * - профиль чужого пользователя рядом с токеном другого не ложится (`setProfile`)
 */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly permissionsService = inject(PermissionsService);
  private readonly workspaceContext = inject(WorkspaceContextService);
  private readonly sessionReset = inject(SessionResetService);

  private readonly session = signal<StoredSession | null>(readSession());

  readonly profile = computed<ProfileModel | null>(() => this.session()?.profile ?? null);
  readonly permissionsByWorkspace = computed<PermissionsByWorkspace>(() => this.session()?.permissionsByWorkspace ?? {});

  /** Свои пространства — ключи прав: членство и есть права (спец. authorization, 1) */
  readonly workspaceIds = computed(() => Object.keys(this.permissionsByWorkspace()));

  /** Пользователь и активное пространство вкладки — одним сигналом, меняется только по существу */
  readonly context = computed<SessionContext>(
    () => ({ userId: this.profile()?.id ?? null, workspaceId: this.workspaceContext.currentWorkspaceId() }),
    { equal: (a, b) => a.userId === b.userId && a.workspaceId === b.workspaceId }
  );

  constructor() {
    // Права активного пространства — из сохранённого: после перезагрузки страницы
    // ни входа, ни переключения не было, и меню иначе осталось бы без пунктов
    this.ensureActiveWorkspace(this.workspaceContext.getCurrentWorkspaceId());

    // Соседняя вкладка вошла, вышла или перевыпустила сессию. Событие приходит только
    // в чужие вкладки — свою запись вкладка не слышит. `key === null` — `clear()`
    if (typeof window !== 'undefined') {
      const onStorage = (event: StorageEvent) => {
        if (event.key === SESSION_KEY || event.key === null) this.syncFromStorage();
      };

      window.addEventListener('storage', onStorage);
      inject(DestroyRef).onDestroy(() => window.removeEventListener('storage', onStorage));
    }
  }

  get isAuthenticated(): boolean {
    return this.session() !== null;
  }

  /**
   * Свежий профиль из ответа сервера (сохранение, аватар, основное пространство).
   *
   * Пишет, только если запись в хранилище — того же человека: пока шёл запрос, соседняя
   * вкладка могла войти другим, и профиль прежнего лёг бы рядом с чужим токеном. Тогда
   * вкладка просто перечитывает запись. Токен и права берутся из хранилища — они свежее
   */
  setProfile(profile: ProfileModel): void {
    const stored = readSession();
    const owned = stored ? stored.profile.id === profile.id : this.session() === null;

    if (!owned) {
      this.syncFromStorage();
      return;
    }

    this.store({ token: stored?.token ?? '', profile, permissionsByWorkspace: stored?.permissionsByWorkspace ?? {} });
  }

  /** Сессия забыта без уборки разделов и без сервера — для спек и сброса состояния */
  clearProfile(): void {
    removeSession();
    this.session.set(null);
    this.switchWorkspace(null);
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
    this.store({
      token: result.token,
      profile: result.profile,
      permissionsByWorkspace: result.permissionsByWorkspace ?? {}
    });

    this.ensureActiveWorkspace(activeWorkspaceId);
  }

  /**
   * Перевыпуск сессии того же человека (смена пароля и логина, подтверждение паролем,
   * слияние): новый токен, а активное пространство вкладки остаётся, раз оно среди своих
   */
  renewSession(result: AuthenticationResultModel): void {
    this.applySession(result, this.workspaceContext.getCurrentWorkspaceId());
  }

  /**
   * Права по пространствам, перечитанные у сервера (`PermissionsRefreshService`): их поменяли
   * чужими руками — выдали, отозвали, убрали из пространства. Токен и профиль те же.
   *
   * Только поверх сессии того же человека, что спрашивал (`userId`): пока шёл запрос,
   * соседняя вкладка могла войти другим. Ничего не поменялось — ничего не пишем: иначе
   * меню и гарды перепроверялись бы на каждый возврат во вкладку. Активное пространство,
   * из которого человека убрали, сменяется на своё (`ensureActiveWorkspace`)
   */
  applyPermissions(permissionsByWorkspace: PermissionsByWorkspace, userId: string): void {
    const stored = readSession();
    if (!stored || stored.profile.id !== userId) {
      this.syncFromStorage();
      return;
    }

    if (JSON.stringify(stored.permissionsByWorkspace) === JSON.stringify(permissionsByWorkspace)) return;

    this.store({ ...stored, permissionsByWorkspace });
    this.ensureActiveWorkspace(this.workspaceContext.getCurrentWorkspaceId());
  }

  /**
   * Сессия после принятия приглашения: активным становится принятое пространство
   * (спец. authorization, 6.3) — единственное, которого в сессии до этого не было
   */
  applyAcceptedInvitation(result: AuthenticationResultModel): void {
    const before = new Set(this.workspaceIds());
    const accepted = Object.keys(result.permissionsByWorkspace ?? {}).find(id => !before.has(id)) ?? null;

    this.applySession(result, accepted);
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
    return this.permissionsByWorkspace()[workspaceId] ?? [];
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
   * Вынужденный выход: сервер не принял токен (401). Зовёт `authInterceptor` с тем
   * токеном, который ушёл в отказанном запросе.
   *
   * Сравнить-и-стереть: в хранилище уже другой токен — его записала соседняя вкладка
   * (новый вход, перевыпуск), и отказ касается только устаревшего запроса этой.
   * Тогда запись не трогаем, а перечитываем. Без токена (`undefined`) — стираем всегда:
   * так кончает сессию удаление аккаунта.
   *
   * Порядок обратный `logOut()`: сначала обнуляем сессию, потом убираем разделы.
   * Ходить на сервер уже не с чем, а синхронное обнуление — то, на что смотрит
   * перехватчик (`isAuthenticated`), чтобы не показать плашку на каждый из нескольких
   * запросов страницы, ответивших 401 одновременно
   */
  async expireSession(rejectedToken?: string): Promise<void> {
    const stored = readSession();

    if (rejectedToken !== undefined && stored != null && stored.token !== rejectedToken) {
      this.syncFromStorage();
      return;
    }

    this.clearSession();
    await this.sessionReset.run({ canPersist: false } satisfies SessionResetOptions);
  }

  /**
   * Запись поменяла соседняя вкладка — догоняем её, **ничего не записывая в ответ**:
   * иначе две вкладки перетирали бы друг друга. Сменился человек (вход другим, выход) —
   * разделы этой вкладки убираются, как при выходе; сервер при этом не трогаем
   */
  private syncFromStorage(): void {
    const stored = readSession();
    const current = this.session();
    if (JSON.stringify(stored) === JSON.stringify(current)) return;

    const userChanged = (stored?.profile.id ?? null) !== (current?.profile.id ?? null);

    this.session.set(stored);
    this.ensureActiveWorkspace(userChanged ? null : this.workspaceContext.getCurrentWorkspaceId());

    if (userChanged) void this.sessionReset.run({ canPersist: false });
  }

  /** Обнуление самого входа: токен, профиль, права, активное пространство */
  private clearSession(): void {
    removeSession();
    this.session.set(null);
    this.switchWorkspace(null);
  }

  private store(session: StoredSession): void {
    writeSession(session);
    this.session.set(session);
  }

  /**
   * Активное пространство обязано быть своим: из пространства, откуда человека убрали
   * или откуда он вышел, запросы не должны уходить с его заголовком
   */
  private ensureActiveWorkspace(preferred: string | null): void {
    const own = this.workspaceIds();
    const primary = this.profile()?.primaryWorkspaceId ?? null;

    const active = [preferred, primary, own[0] ?? null].find(id => id != null && own.includes(id)) ?? null;

    this.switchWorkspace(active);
  }
}
