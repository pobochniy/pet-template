import { LazyScope } from '../shared/i18n/generated/i18n-keys';

/**
 * Настройки пространств и участники: выдача и отзыв приглашения отвечают кодами `invite.*`.
 *
 * Один список на маршрут и его экраны: его грузит `withScope` в `app.routes.ts`,
 * и его же принимает `injectI18n(...)` экранов — ключ чужого словаря не скомпилируется.
 * `npm run i18n:codes` читает эти константы, чтобы знать, какие словари грузятся
 */
export const WORKSPACE_SCOPES = ['workspace', 'invite'] as const satisfies readonly LazyScope[];
