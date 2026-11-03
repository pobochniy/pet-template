import { ChangeDetectionStrategy, Component, DestroyRef, Injector, inject } from '@angular/core';
import { recheckRouteAccess } from './route-access';
import { RouterOutlet } from '@angular/router';
import { AlertsComponent } from './shared/alerts/alerts.component';
import { NavMenuComponent } from './shared/nav/menu/menu.component';
import { TopNavComponent } from './shared/nav/top-nav/top-nav.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, AlertsComponent, NavMenuComponent, TopNavComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class App {
  constructor() {
    recheckRouteAccess();

    // Права, поменянные чужими руками, — перечитать на возврат во вкладку (спец. authorization, 2.2).
    // `import()`: в initial — только этот вызов, сам сервис — своим чанком
    const injector = inject(Injector);
    let destroyed = false;
    inject(DestroyRef).onDestroy(() => (destroyed = true));
    void import('./shared/core/permissions-refresh.service').then(m => {
      if (!destroyed) injector.get(m.PermissionsRefreshService).watch();
    });
  }
}
