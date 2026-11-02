import { EventEmitter, Injectable } from '@angular/core';

/** Кнопка меню в шапке: сворачивает и разворачивает боковое меню */
@Injectable({
  providedIn: 'root'
})
export class EventEmitterService {
  readonly invokeMenuToggleMenuFunction = new EventEmitter<void>();

  onToggleMenuButtonClick() {
    this.invokeMenuToggleMenuFunction.emit();
  }
}
