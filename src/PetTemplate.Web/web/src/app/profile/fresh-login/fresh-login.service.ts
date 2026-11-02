import { Injectable, inject, signal } from '@angular/core';
import { AccountLoginsModel } from '../../shared/models/account.model';
import { SessionResetService } from '../../shared/core/session-reset.service';

/** Отказ сервера «сначала подтвердите, что это вы» (8.6) */
export const FRESH_LOGIN_REQUIRED = 'auth.error.freshLoginRequired';

/**
 * Запас до конца окна: привязка и слияние через площадку уходят на её страницу
 * и возвращаются через минуту-другую — подтверждение должно их пережить
 */
const MARGIN_MS = 2 * 60_000;

/**
 * Подтверждение владения аккаунтом (спец. accounts, 8.6): привязка, отвязка, смена почты
 * и слияние — только в окне свежего входа. Срок окна приходит в «Основное»
 * (`freshLoginUntilUtc`); закрыто — действие ждёт, пока человек подтвердит паролем
 * или войдёт заново через площадку. Сам запрос показывает `profile-fresh-login`,
 * а действие только ждёт ответа `ensure()`.
 *
 * Окно — по часам сервера, сверка — по часам браузера: разъедутся — сервер откажет
 * `auth.error.freshLoginRequired`, и `run()` спросит подтверждение ещё раз.
 *
 * Лежит у профиля, а не в `shared/core`: нужен только его экранам и уезжает в их
 * ленивый чанк. Слияние (`profile-merge-start`) зовёт тот же `ensure()`/`run()`
 */
@Injectable({ providedIn: 'root' })
export class FreshLoginService {
  /** Действие ждёт подтверждения */
  readonly pending = signal(false);

  private until: number | null = null;
  private resolve: ((confirmed: boolean) => void) | null = null;

  constructor() {
    inject(SessionResetService).register(() => this.forget());
  }

  /** Срок окна из «Основное» — после каждого ответа, который его несёт */
  track(logins: AccountLoginsModel): void {
    this.until = logins.freshLoginUntilUtc ? Date.parse(logins.freshLoginUntilUtc) : null;
  }

  isFresh(): boolean {
    return this.until != null && this.until - Date.now() > MARGIN_MS;
  }

  /** Окно открыто — сразу `true`; иначе — когда человек подтвердит (`true`) или передумает (`false`) */
  ensure(): Promise<boolean> {
    if (this.isFresh()) return Promise.resolve(true);

    // Второе действие, пока ждёт первое, — первое отменяется: подтверждение одно на экран
    this.finish(false);
    this.pending.set(true);
    return new Promise(resolve => (this.resolve = resolve));
  }

  /**
   * Действие в окне подтверждения. Сервер всё же отказал (окно закрылось по дороге) —
   * спросить ещё раз и повторить. Передумал — `null`
   */
  async run<T>(action: () => Promise<T>): Promise<T | null> {
    if (!(await this.ensure())) return null;

    try {
      return await action();
    } catch (e: unknown) {
      if ((e as { error?: { code?: unknown } } | null)?.error?.code !== FRESH_LOGIN_REQUIRED) throw e;

      this.until = null;
      if (!(await this.ensure())) return null;
      return await action();
    }
  }

  /** Подтвердили паролем — `profile-fresh-login` с новыми «Основное» */
  confirmed(logins: AccountLoginsModel): void {
    this.track(logins);
    this.finish(true);
  }

  cancel(): void {
    this.finish(false);
  }

  private finish(confirmed: boolean): void {
    this.pending.set(false);
    const resolve = this.resolve;
    this.resolve = null;
    resolve?.(confirmed);
  }

  private forget(): void {
    this.until = null;
    this.finish(false);
  }
}
