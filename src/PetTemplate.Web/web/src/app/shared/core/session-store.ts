import { ProfileModel } from '../models/profile.model';
import { PermissionsByWorkspace } from '../models/authentication-result.model';
import { safeStorage } from './safe-storage';

/**
 * Ключ сессии в `localStorage`: токен и пользователь — **одной записью**.
 *
 * Врозь они разъезжались между вкладками: вкладка с чужим (старым) токеном затирала
 * свежий, записанный соседней, а профиль одного пользователя ложился рядом с токеном
 * другого. Одна запись пишется и читается целиком — половины сессии не бывает
 */
export const SESSION_KEY = 'session';

/** Ключи прежней раскладки (токен и пользователь отдельно) — читаются один раз при переезде */
const LEGACY_TOKEN_KEY = 'token';
const LEGACY_USER_KEY = 'user';

/** Сессия так, как она лежит в хранилище */
export interface StoredSession {
  readonly token: string;
  readonly profile: ProfileModel;
  readonly permissionsByWorkspace: PermissionsByWorkspace;
}

/**
 * Сессия из хранилища — всегда заново, без кеша в памяти: соседняя вкладка могла
 * её только что заменить. Битая запись — входа нет
 */
export function readSession(): StoredSession | null {
  const stored = safeStorage.getJson<Partial<StoredSession>>(SESSION_KEY);
  if (stored != null) return normalize(stored);

  return migrateLegacy();
}

export function writeSession(session: StoredSession): void {
  safeStorage.setJson(SESSION_KEY, session);
}

export function removeSession(): void {
  safeStorage.remove(SESSION_KEY);
}

function normalize(stored: Partial<StoredSession> | null): StoredSession | null {
  if (stored?.profile == null || typeof stored.profile !== 'object') return null;

  return {
    token: typeof stored.token === 'string' ? stored.token : '',
    profile: stored.profile,
    permissionsByWorkspace: stored.permissionsByWorkspace ?? {}
  };
}

/**
 * Вход, сохранённый до объединения записей, не выкидывает человека после выката:
 * токен и пользователь собираются в одну запись, старые ключи уходят
 */
function migrateLegacy(): StoredSession | null {
  const user = safeStorage.getJson<Partial<StoredSession>>(LEGACY_USER_KEY);
  const token = safeStorage.get(LEGACY_TOKEN_KEY);
  if (user == null && token == null) return null;

  safeStorage.remove(LEGACY_USER_KEY);
  safeStorage.remove(LEGACY_TOKEN_KEY);

  const session = normalize(user ? { ...user, token: token ?? '' } : null);
  if (session) writeSession(session);

  return session;
}
