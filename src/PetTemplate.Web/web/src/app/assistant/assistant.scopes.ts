import { LazyScope } from '../shared/i18n/generated/i18n-keys';

/**
 * Чат с ассистентом и его части (плашка лимита, голос) — их же показывает главная.
 *
 * Один список на маршрут и его экраны: его грузит `withScope` в `app.routes.ts`,
 * и его же принимает `injectI18n(...)` экранов — ключ чужого словаря не скомпилируется.
 * `npm run i18n:codes` читает эти константы, чтобы знать, какие словари грузятся
 */
export const ASSISTANT_SCOPES = ['assistant'] as const satisfies readonly LazyScope[];
