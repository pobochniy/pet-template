import { ActivatedRouteSnapshot, ViewTransitionInfo } from '@angular/router';
import { isAbortedViewTransition, onViewTransitionCreated } from './app.config';

/**
 * Настройка перехода между экранами: где перелистывание нужно, а где оно
 * моргает тем же составом разметки.
 */
describe('onViewTransitionCreated', () => {
  /** Снимок маршрута: решает конечный в цепочке, поэтому важен только `routeConfig` */
  function snapshot(routeConfig: object | null, child?: ActivatedRouteSnapshot): ActivatedRouteSnapshot {
    return { routeConfig, firstChild: child ?? null } as unknown as ActivatedRouteSnapshot;
  }

  function transition() {
    return { skipTransition: vi.fn() };
  }

  function run(from: ActivatedRouteSnapshot, to: ActivatedRouteSnapshot) {
    const view = transition();
    onViewTransitionCreated({ transition: view, from, to } as unknown as ViewTransitionInfo);

    return view;
  }

  // Внутри одного маршрута разметка та же — перелистывание дало бы только моргание
  it('should skip the transition when only a parameter of the same route changes', () => {
    const profileView = { path: 'view/:userId' };
    const view = run(snapshot({}, snapshot(profileView)), snapshot({}, snapshot(profileView)));

    expect(view.skipTransition).toHaveBeenCalled();
  });

  it('should keep the transition between sections', () => {
    const view = run(snapshot({}, snapshot({ path: 'profile' })), snapshot({}, snapshot({ path: 'auth' })));

    expect(view.skipTransition).not.toHaveBeenCalled();
  });

  /** Маршрута без конфигурации быть не должно, но моргать из-за него — тем более */
  it('should not skip the transition when the route has no config', () => {
    const view = run(snapshot(null), snapshot(null));

    expect(view.skipTransition).not.toHaveBeenCalled();
  });
});

/**
 * Отменённое браузером перелистывание доезжает до `catch` вокруг запуска
 * приложения и печаталось там как ошибка запуска. Гасить его можно, только
 * отличив от настоящей поломки, — этим и занята проверка
 */
describe('isAbortedViewTransition', () => {
  it('should recognize a transition aborted by the browser', () => {
    const error = new DOMException(
      'Transition was aborted because of invalid state. Viewport size changed',
      'InvalidStateError');

    expect(isAbortedViewTransition(error)).toBe(true);
  });

  /** Тем же именем отвечают и настоящие поломки — их глушить нельзя */
  it('should not match other InvalidStateError', () => {
    expect(isAbortedViewTransition(new DOMException('Document is not active', 'InvalidStateError')))
      .toBe(false);
  });

  it('should not match ordinary startup errors', () => {
    expect(isAbortedViewTransition(new Error('NG0200: Circular dependency'))).toBe(false);
    expect(isAbortedViewTransition('string')).toBe(false);
    expect(isAbortedViewTransition(null)).toBe(false);
  });
});
