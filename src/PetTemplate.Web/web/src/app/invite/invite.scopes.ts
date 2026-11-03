import { LazyScope } from '../shared/i18n/generated/i18n-keys';

/**
 * Лендинг приглашения: регистрация по ссылке отказывает кодами `auth.error.*`.
 *
 * Один список на маршрут и его экраны: его грузит `withScope` в `app.routes.ts`,
 * и его же принимает `injectI18n(...)` экранов — ключ чужого словаря не скомпилируется.
 * `npm run i18n:codes` читает эти константы, чтобы знать, какие словари грузятся
 */
export const INVITE_SCOPES = ['invite', 'auth'] as const satisfies readonly LazyScope[];
