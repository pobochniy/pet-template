import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminApiService } from '../core/admin-api.service';
import { AdminSessionService } from '../core/admin-session.service';
import { apiErrorText } from '../core/api-error';
import { AdminUnavailableState } from '../core/admin.guard';

/**
 * Вход логином и паролем от продукта (спец. admin, 3.3 и 4). Сервер на любой отказ
 * отвечает одним `404` — неверный пароль, нет ролей, исчерпан лимит попыток, —
 * и различить их отсюда нельзя намеренно.
 *
 * Сюда же ведёт `adminGuard`, когда `Admin/Me` не ответил (сеть, `5xx`): токен цел,
 * и «повторить» снова пробует открыть оболочку
 */
@Component({
  selector: 'adm-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="container py-5" style="max-width: 360px">
      <h1 class="h5 mb-3">PetTemplate · админка</h1>
      @if (unavailable()) {
        <div class="alert alert-warning small unavailable">
          Сервер не ответил ({{ unavailable() }}). Вход не сброшен —
          <button class="btn btn-link btn-sm p-0 align-baseline retry" type="button" (click)="retry()">повторить</button>
        </div>
      }
      <form (ngSubmit)="submit()">
        <div class="mb-2">
          <input class="form-control" name="login" placeholder="Логин" autocomplete="username"
                 [(ngModel)]="login" required>
        </div>
        <div class="mb-3">
          <input class="form-control" name="password" type="password" placeholder="Пароль"
                 autocomplete="current-password" [(ngModel)]="password" required>
        </div>
        <button class="btn btn-primary w-100" type="submit" [disabled]="busy() || !login || !password">Войти</button>
      </form>
      @if (error()) {
        <div class="alert alert-danger mt-3 mb-0 small">Не вошли: {{ error() }}</div>
      }
    </div>
  `
})
export class LoginComponent {
  private readonly api = inject(AdminApiService);
  private readonly session = inject(AdminSessionService);
  private readonly router = inject(Router);

  protected login = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Почему не открылась оболочка при живом токене — из состояния навигации guard'а */
  protected readonly unavailable = signal<string | null>(
    (this.router.currentNavigation()?.extras.state as Partial<AdminUnavailableState> | undefined)
      ?.adminUnavailable ?? null);

  protected retry(): void {
    this.unavailable.set(null);
    void this.router.navigateByUrl('/');
  }

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      this.session.start(await this.api.login(this.login, this.password));
      await this.router.navigateByUrl('/');
    } catch (error) {
      // Голый `404` — одной человеческой фразой на все причины (спец. admin, 4)
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 404
          ? 'неверный логин или пароль, нет ролей в админке или слишком много попыток'
          : apiErrorText(error)
      );
    } finally {
      this.busy.set(false);
    }
  }
}
