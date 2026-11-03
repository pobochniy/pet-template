import { Provider } from '@angular/core';

/**
 * Один список провайдеров на все сборки. Режима «без бэкенда» нет: моки живут
 * только в спеках, см. `src/PetTemplate.Web/agents.md`, «Mocks & DI».
 *
 * Здесь — только то, что нужно оболочке. Api-сервисы экранов (`AuthApiService`,
 * `ProfileApiService`, `UsersApiService`, `WorkspaceApiService`) — `providedIn: 'root'`:
 * статический импорт сюда утаскивал их в initial-бандл, а так они едут в чанк своего
 * экрана (`tech-debt/bundle.md`). Сейчас оболочке своих нет
 */
export const serviceCollection: Provider[] = [];
