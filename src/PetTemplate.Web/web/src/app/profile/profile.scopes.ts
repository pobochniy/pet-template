import { LazyScope } from '../shared/i18n/generated/i18n-keys';

/**
 * Профиль: свой словарь и `auth` — отказы своего аккаунта («логин занят», «почта занята»,
 * «неверный текущий пароль») приезжают кодами `auth.error.*` (спец. accounts, 6).
 *
 * Один список на маршрут и его экраны: его грузит `withScope` в `app.routes.ts`,
 * и его же принимает `injectI18n(...)` экранов — ключ чужого словаря не скомпилируется.
 * `npm run i18n:codes` читает эти константы, чтобы знать, какие словари грузятся
 */
export const PROFILE_SCOPES = ['profile', 'auth'] as const satisfies readonly LazyScope[];

/** Память ассистента (спец. assistant, 12.5): поверх профиля — словарь ассистента (`loadScope` в `profile.routes.ts`) */
export const ASSISTANT_MEMORY_SCOPES = [...PROFILE_SCOPES, 'assistant'] as const satisfies readonly LazyScope[];
