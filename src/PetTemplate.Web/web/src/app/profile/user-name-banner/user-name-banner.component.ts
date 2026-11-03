import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { safeStorage } from '../../shared/core/safe-storage';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { PROFILE_SCOPES } from '../profile.scopes';

/** «Не сейчас» прячет плашку на сутки (спец. accounts, 4.2) */
export const USER_NAME_BANNER_SNOOZE_MS = 24 * 60 * 60 * 1000;

/** Ключ `localStorage` — по id аккаунта: на общем устройстве у каждого своё «не сейчас» */
export function userNameBannerKey(userId: string): string {
  return `pettemplate.userNameBanner.snoozedAt.${userId}`;
}

/**
 * Плашка автоматического логина (спец. accounts, 4.2): «Ваш логин — tg123456789. По нему
 * вас находят участники пространства — придумайте понятный», с переходом к полю логина.
 *
 * Видна, пока логин автоматический (`generated`). «Не сейчас» запоминает время нажатия
 * в `localStorage` по id аккаунта и прячет плашку на сутки. Поля в модели под это нет —
 * на другом устройстве плашка покажется сразу, это допустимо. Хранилище может быть
 * недоступно (приватный режим, запрет сайту) — тогда чтение и запись молча не удаются,
 * и плашка остаётся видна: лишний показ лучше, чем упавший экран.
 *
 * Автоматический логин получает аккаунт, заведённый входом через Telegram или VK —
 * на сайте и из mini-app (спец. accounts, 4.2)
 */
@Component({
  selector: 'profile-user-name-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <div class="alert alert-info d-flex flex-wrap align-items-center gap-2 mb-0" id="user-name-banner" role="status">
        <span class="flex-grow-1">{{ i18n.t('profile.userNameBanner.text', { login: userName() }) }}</span>
        <button type="button" class="btn btn-sm btn-primary" id="user-name-banner-change" (click)="changeRequested.emit()">
          {{ i18n.t('profile.userNameBanner.change') }}
        </button>
        <button type="button" class="btn btn-sm btn-outline-secondary" id="user-name-banner-later" (click)="snooze()">
          {{ i18n.t('profile.userNameBanner.later') }}
        </button>
      </div>
    }
  `
})
export class UserNameBannerComponent {
  protected readonly i18n = injectI18n(...PROFILE_SCOPES);

  readonly userId = input.required<string>();
  readonly userName = input.required<string>();
  readonly generated = input.required<boolean>();

  /** «Сменить логин» — родителю поставить фокус в поле логина */
  readonly changeRequested = output<void>();

  /** Перерисовка после «Не сейчас»: само хранилище сигналом не является */
  private readonly snoozed = signal(false);

  protected readonly visible = computed(() =>
    this.generated() && this.userId().length > 0 && !this.snoozed() && !isSnoozed(this.userId(), Date.now()));

  protected snooze(): void {
    // Хранилище недоступно — «не сейчас» живёт в памяти страницы (`safeStorage`)
    safeStorage.set(userNameBannerKey(this.userId()), String(Date.now()));

    this.snoozed.set(true);
  }
}

function isSnoozed(userId: string, now: number): boolean {
  const stored = Number(safeStorage.get(userNameBannerKey(userId)));
  return Number.isFinite(stored) && stored > 0 && now - stored < USER_NAME_BANNER_SNOOZE_MS;
}
