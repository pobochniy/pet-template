import { LazyScope } from '../shared/i18n/generated/i18n-keys';

/**
 * Главная: свой словарь и `assistant` — строка ассистента и плашка лимита на её месте (спец. assistant, 2 и 13.6).
 *
 * Один список на маршрут и его экраны: его грузит `withScope` в `app.routes.ts`,
 * и его же принимает `injectI18n(...)` экранов — ключ чужого словаря не скомпилируется.
 * `npm run i18n:codes` читает эти константы, чтобы знать, какие словари грузятся
 */
export const HOME_SCOPES = ['home', 'assistant'] as const satisfies readonly LazyScope[];
