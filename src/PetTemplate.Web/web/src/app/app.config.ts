import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import {
  ActivatedRouteSnapshot, provideRouter, ViewTransitionInfo, withComponentInputBinding, withViewTransitions
} from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './shared/interceptors/auth.interceptor';
import { workspaceInterceptor } from './shared/interceptors/workspace.interceptor';
import { serviceCollection } from './app.providers';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding(), withViewTransitions({ onViewTransitionCreated })),
    provideHttpClient(withInterceptors([authInterceptor, workspaceInterceptor])),
    ...serviceCollection
  ]
};

/**
 * Плавное перелистывание оставляем переходам между разделами. Внутри одного
 * маршрута меняется только параметр адреса (`/profile/view/:userId` → `/profile/view/:userId`) — разметка
 * вокруг та же, и снимок всего документа давал моргание.
 *
 * Экспортируется ради спеки: иначе решение живёт в конфиге, который ничем
 * не проверяется.
 *
 * Гасить здесь отказы промисов перехода бесполезно, хотя и просится: свою
 * отмену печатает сам роутер — `transition.ready.catch(console.error)`
 * внутри `createViewTransition`, и только при `ngDevMode`. Своего обработчика
 * это не отменяет, а снять чужой неоткуда
 */
export function onViewTransitionCreated({ transition, from, to }: ViewTransitionInfo): void {
  // Приезжает корень дерева маршрутов, а решает конечный — до него и спускаемся
  const fromConfig = deepestRoute(from).routeConfig;

  if (fromConfig && fromConfig === deepestRoute(to).routeConfig) transition.skipTransition();
}

function deepestRoute(snapshot: ActivatedRouteSnapshot): ActivatedRouteSnapshot {
  let route = snapshot;
  while (route.firstChild) route = route.firstChild;

  return route;
}

/**
 * Отмена перелистывания браузером, а не поломка приложения.
 *
 * Браузер отменяет переход, если во время него поменялся размер вьюпорта —
 * например, у длинного списка появилась полоса прокрутки. Отказ доезжает
 * до `catch` в `main.ts` и печатается там как ошибка запуска, хотя приложение
 * уже работает: перелистывание — украшение, при отмене страница просто
 * рисуется сразу.
 *
 * Сверяем и текст, а не только имя: `InvalidStateError` в `catch` вокруг
 * запуска приложения бывает и настоящей поломкой, и глушить его целиком
 * нельзя. Текст английский и не локализуется, но опора на него всё равно
 * хрупкая — поэтому проверка живёт в одном месте и под тестом.
 */
export function isAbortedViewTransition(error: unknown): boolean {
  return error instanceof DOMException
    && error.name === 'InvalidStateError'
    && /transition was aborted/i.test(error.message);
}
