import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { appConfig, isAbortedViewTransition } from './app/app.config';

bootstrapApplication(App, appConfig)
  .catch(err => {
    // Отменённое браузером перелистывание доезжает сюда отказом промиса
    // и печаталось как ошибка запуска, хотя приложение уже работает.
    // Гасим только его — всё остальное в этом `catch` означает, что
    // приложение не поднялось, и молчать об этом нельзя
    if (isAbortedViewTransition(err)) return;

    console.error(err);
  });
