import { Injectable, signal } from '@angular/core';

/**
 * Развёрнуто ли боковое меню. Кнопка в шапке и само меню — соседи, а не родитель
 * с ребёнком, поэтому состояние живёт в общем сервисе — сигналом, а не шиной событий:
 * меню читает его в шаблоне и перерисовывается само
 */
@Injectable({ providedIn: 'root' })
export class MenuStateService {
  private readonly _expanded = signal(false);

  readonly expanded = this._expanded.asReadonly();

  toggle(): void {
    this._expanded.update(value => !value);
  }

  collapse(): void {
    this._expanded.set(false);
  }
}
