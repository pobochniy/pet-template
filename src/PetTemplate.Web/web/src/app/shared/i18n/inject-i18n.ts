import { inject } from '@angular/core';
import { I18nParams, I18nService } from './i18n.service';
import { LazyScope, ScopedKey, ScopedPluralKey } from './generated/i18n-keys';

/**
 * `I18nService`, у которого `t` и `plural` принимают ключи только `core` и названных
 * словарей. Тот же объект — меняется только тип: ни памяти, ни байтов в бандле
 */
export type ScopedI18n<S extends LazyScope> = Omit<I18nService, 't' | 'plural'> & {
  t(key: ScopedKey<'core' | S>, params?: I18nParams): string;
  plural(key: ScopedPluralKey<'core' | S>, count: number, params?: I18nParams): string;
};

/**
 * Перевод для экрана с проверкой словарей на компиляции.
 *
 * `I18nKey` — объединение ключей **всех** словарей, и `t('invite.x')` компилировался бы
 * в компоненте, чей маршрут словарь `invite` не грузит: в интерфейсе вышло бы `⟦invite.x⟧`.
 * Здесь `t` принимает только `core` и перечисленные словари — те, что грузит маршрут.
 * Список один на маршрут и его экраны: константа `*_SCOPES` из `{раздел}/{раздел}.scopes.ts`
 * уходит и в `withScope(...)` в `app.routes.ts`, и сюда:
 *
 * ```ts
 * protected readonly i18n = injectI18n(...WORKSPACE_SCOPES);
 * ```
 *
 * Без аргументов — только `core`: так берут перевод оболочка и общие компоненты.
 * Компонент, который рисует ключ, присланный снаружи (`form-validation`), берёт
 * `inject(I18nService)` — проверять там нечего
 */
export function injectI18n<const S extends readonly LazyScope[] = []>(...scopes: S): ScopedI18n<S[number]> {
  void scopes;

  return inject(I18nService) as unknown as ScopedI18n<S[number]>;
}
