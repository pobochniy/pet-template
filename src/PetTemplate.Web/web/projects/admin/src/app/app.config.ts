import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { adminInterceptor } from './core/admin.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes),
    // Один интерцептор: только `Authorization`. `X-Workspace-Id` у админки нет —
    // workspace у её запросов не бывает (спец. admin, 3.5 и 4)
    provideHttpClient(withInterceptors([adminInterceptor]))
  ]
};
