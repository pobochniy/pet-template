import { effect, inject } from '@angular/core';
import { Router } from '@angular/router';
import { UserService } from './shared/core/user.service';
import { PermissionsService } from './shared/services/permissions.service';

/**
 * Гарды маршрута срабатывают только на переходе. Сменили пространство в шапке или
 * в соседней вкладке вошёл другой человек — открытый экран остаётся, хотя права
 * у него уже другие. Поэтому на смену контекста вкладки (`UserService.context`) тот же
 * адрес проходит навигацию ещё раз (`onSameUrlNavigation: 'reload'`), а маршруты
 * с гардами объявлены с `runGuardsAndResolvers: 'always'` (`requireRoles`, `signedInGuard` в `app.routes.ts`):
 * пускают — экран остаётся на месте, нет — гард уводит на главную или на вход.
 *
 * То же — когда в том же пространстве поменялись сами права: их перечитали у сервера
 * (`PermissionsRefreshService`), потому что их выдали или отозвали чужими руками.
 *
 * Зовётся один раз — из корневого компонента
 */
export function recheckRouteAccess(): void {
  const user = inject(UserService);
  const router = inject(Router);
  const permissions = inject(PermissionsService);

  // Права — по содержимому: `switchWorkspace` кладёт новый массив и при тех же правах
  const rights = () => [...permissions.permissions()].sort().join(',');
  let checkedFor = user.context();
  let checkedRights = rights();

  effect(() => {
    const context = user.context();
    const current = rights();
    if (context === checkedFor && current === checkedRights) return;

    checkedFor = context;
    checkedRights = current;

    // До первой навигации проверять нечего, а идущая сама проверит гарды уже по новому
    // контексту: так выход ведёт на вход, а не обратно на прежний адрес. Чтение
    // `currentNavigation` эффект запомнит, но следующий его запуск выйдет на сверке
    // контекста выше — лишней навигации он не сделает
    if (!router.navigated || router.currentNavigation() != null) return;

    void router.navigateByUrl(router.url, { onSameUrlNavigation: 'reload', replaceUrl: true });
  });
}
