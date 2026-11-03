import { Injectable } from '@angular/core';

/**
 * Условия уборки. Отличают добровольный выход от вынужденного: в первом случае
 * токен ещё действует, во втором его уже не приняли
 */
export interface SessionResetOptions {
  /**
   * Дойдёт ли запрос до сервера. `false` — токен недействителен (401), и обработчику
   * остаётся только забыть своё состояние: запрос всё равно не пройдёт,
   * а пользователю добавит красную плашку поверх формы входа
   */
  readonly canPersist: boolean;
}

/** Уборка одного сервиса. Может быть асинхронной — обработчику может понадобиться сервер */
export type SessionResetHandler = (options: SessionResetOptions) => Promise<void> | void;

/**
 * Очистка состояния при выходе из аккаунта.
 *
 * Корневые сервисы переживают выход: следующий пользователь в той же вкладке
 * до первого ответа сервера видел бы данные предыдущего — кеши разделов, треды
 * ассистента (спец. assistant, 2.2). Звать их из `UserService`
 * напрямую нельзя — ядро не должно знать про разделы и `shared/api`, поэтому
 * владелец состояния сам записывается на уборку, а выход (`UserService.logOut`)
 * только объявляет её.
 *
 * Сама сессия — токен, профиль, права, активное пространство — сюда не записывается:
 * её обнуляет `UserService`, после уборки (см. там же, почему порядок важен).
 *
 * Записываются в конструкторе, то есть только те сервисы, которые в этом заходе
 * действительно создавали, — у остальных и чистить нечего.
 */
@Injectable({ providedIn: 'root' })
export class SessionResetService {
  private readonly handlers = new Set<SessionResetHandler>();

  /** Возвращает отписку — она нужна тестам, в приложении сервисы живут до конца сессии */
  register(handler: SessionResetHandler): () => void {
    this.handlers.add(handler);

    return () => {
      this.handlers.delete(handler);
    };
  }

  /**
   * Уборка по очереди: обработчики могут ходить на сервер, и параллельный запуск
   * ничего бы не ускорил.
   *
   * Ошибка одного не отменяет остальных и не срывает сам выход: держать
   * пользователя в аккаунте из-за неудачной уборки нельзя.
   */
  async run(options: SessionResetOptions = { canPersist: true }): Promise<void> {
    for (const handler of this.handlers) {
      try {
        await handler(options);
      } catch (error) {
        console.error('Failed to reset session state', error);
      }
    }
  }
}
