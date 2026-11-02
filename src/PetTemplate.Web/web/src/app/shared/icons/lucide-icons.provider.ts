import { Provider } from '@angular/core';
import { LUCIDE_ICONS, LucideIconProvider, LucideIcons } from 'lucide-angular';

/**
 * Регистрация иконок на уровне компонента, а не приложения: набор уезжает
 * в тот же чанк, что и компонент, поэтому иконки ленивых экранов не попадают
 * в initial-бандл.
 *
 * Штатный `LucideAngularModule.pick()` для этого не подходит: он возвращает
 * ModuleWithProviders, а standalone-компонент такое в `imports` не принимает
 * (NG2012). Провайдер собираем руками, `imports` при этом всё равно нужен —
 * оттуда берётся сама директива `i-lucide`.
 */
export function provideIcons(icons: LucideIcons): Provider {
  return { provide: LUCIDE_ICONS, multi: true, useValue: new LucideIconProvider(icons) };
}
