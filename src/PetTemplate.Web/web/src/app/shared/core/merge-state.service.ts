import { Injectable, inject, signal } from '@angular/core';
import { MergePreviewModel } from '../models/account.model';
import { SessionResetService } from './session-reset.service';

/**
 * Просмотр слияния между экранами (спец. accounts, 7.3): его отдают привязка занятого входа,
 * развилка mini-app и «Объединить с другим аккаунтом», а показывает `/profile/merge`. В адрес
 * он не кладётся — там ключ слияния, — поэтому живёт здесь до подтверждения или ухода. Выход
 * из аккаунта его забывает: следующий в той же вкладке увидел бы карточки обоих аккаунтов
 */
@Injectable({ providedIn: 'root' })
export class MergeStateService {
  readonly preview = signal<MergePreviewModel | null>(null);

  constructor() {
    inject(SessionResetService).register(() => this.clear());
  }

  open(preview: MergePreviewModel): void {
    this.preview.set(preview);
  }

  clear(): void {
    this.preview.set(null);
  }
}
