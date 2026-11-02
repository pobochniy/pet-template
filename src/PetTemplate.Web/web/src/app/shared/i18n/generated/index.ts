/**
 * Сгенерировано `npm run i18n` из `i18n/`. Руками не правим — правка уедет
 * при следующем запуске кодогена, а CI отобьёт расхождение с исходником.
 */

import { Dict } from '../dict';
import { Lang } from '../lang';
import { LazyScope, Scope } from './i18n-keys';

import enCore from './en.core';
import ruCore from './ru.core';

/** Словарь, который лежит в initial-бандле: он нужен на каждом экране */
export const CORE: Readonly<Record<Lang, Dict>> = {
  en: enCore,
  ru: ruCore,
};

/** Словари экранов — по одному чанку на язык и экран */
export const LOADERS: Readonly<Record<Lang, Readonly<Record<LazyScope, () => Promise<{ default: Dict }>>>>> = {
  en: {
    "assistant": () => import('./en.assistant'),
    "auth": () => import('./en.auth'),
    "home": () => import('./en.home'),
    "invite": () => import('./en.invite'),
    "miniapp": () => import('./en.miniapp'),
    "profile": () => import('./en.profile'),
    "workspace": () => import('./en.workspace'),
  },
  ru: {
    "assistant": () => import('./ru.assistant'),
    "auth": () => import('./ru.auth'),
    "home": () => import('./ru.home'),
    "invite": () => import('./ru.invite'),
    "miniapp": () => import('./ru.miniapp'),
    "profile": () => import('./ru.profile'),
    "workspace": () => import('./ru.workspace'),
  },
};

/** Все словари по именам — состав проверяет `i18n-parity.spec.ts` */
export const SCOPES: readonly Scope[] = ["assistant","auth","core","home","invite","miniapp","profile","workspace"];
