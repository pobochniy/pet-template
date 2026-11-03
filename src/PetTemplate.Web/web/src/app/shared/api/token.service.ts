import { Injectable } from '@angular/core';
import { readSession } from '../core/session-store';

/**
 * Токен текущей сессии — для `authInterceptor`.
 *
 * Своего кеша нет намеренно: токен читается из записи сессии (`session-store.ts`)
 * на каждый запрос. Соседняя вкладка могла перевыпустить сессию (смена пароля
 * отзывает прежние токены) — запрос уйдёт уже со свежим. Записывает сессию только
 * `UserService`
 */
@Injectable({ providedIn: 'root' })
export class TokenService {
  get Token(): string {
    return readSession()?.token ?? '';
  }
}
