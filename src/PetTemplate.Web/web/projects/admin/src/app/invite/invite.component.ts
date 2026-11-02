import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AdminApiService } from '../core/admin-api.service';
import { AdminInvitationStatus } from '../core/admin-invitation-status';
import { AdminRole, ROLE_TITLES } from '../core/admin-role';
import { AdminInvitationPreview } from '../core/admin.model';
import { AdminSessionService } from '../core/admin-session.service';
import { apiErrorText } from '../core/api-error';

/** Что сказать по ссылке, которая уже не работает */
const DEAD_TITLES: Partial<Record<AdminInvitationStatus, string>> = {
  [AdminInvitationStatus.Accepted]: 'По этой ссылке уже пришли — она одноразовая.',
  [AdminInvitationStatus.Revoked]: 'Приглашение отозвано.',
  [AdminInvitationStatus.Expired]: 'Срок приглашения вышел.'
};

/**
 * Страница приглашения `/invite/{token}` (спец. admin, 3.4): кто позвал, какие
 * роли, и вход логином и паролем от продукта — своего пароля у админки нет. Принятие
 * отвечает сразу админской сессией, второй раз входить не нужно.
 *
 * Отказ принятия — голый `404`, как у входа: неверный пароль, исчерпанный лимит или
 * ссылка, умершая, пока открыта страница, — отсюда не различить
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'adm-invite',
  imports: [FormsModule],
  template: `
    <div class="container py-5" style="max-width: 420px">
      <h1 class="h5 mb-3">PetTemplate · админка</h1>

      @if (missing()) {
        <div class="alert alert-secondary small dead">Такого приглашения нет. Проверьте ссылку.</div>
      } @else if (preview(); as preview) {
        @if (preview.status === Status.Pending) {
          <p class="mb-1 invitation">
            <b>{{ preview.inviterName ?? 'Админ' }}</b> приглашает вас в админку с ролями:
            {{ rolesTitle(preview.roles) }}.
          </p>
          <p class="text-body-secondary small mb-3">
            Войдите логином и паролем от продукта. Аккаунта ещё нет — сначала заведите его на
            <a href="https://example.com" target="_blank" rel="noopener">example.com</a> и вернитесь по этой же
            ссылке: приглашение не сгорит.
          </p>
          <form (ngSubmit)="accept()">
            <div class="mb-2">
              <input class="form-control" name="login" placeholder="Логин, почта или телефон" autocomplete="username"
                     [(ngModel)]="login" required>
            </div>
            <div class="mb-3">
              <input class="form-control" name="password" type="password" placeholder="Пароль"
                     autocomplete="current-password" [(ngModel)]="password" required>
            </div>
            <button class="btn btn-primary w-100" type="submit" [disabled]="busy() || !login || !password">
              Принять и войти
            </button>
          </form>
        } @else {
          <div class="alert alert-secondary small dead">{{ deadTitle(preview.status) }}</div>
        }
      }

      @if (error()) {
        <div class="alert alert-danger mt-3 mb-0 small">Не приняли: {{ error() }}</div>
      }
    </div>
  `
})
export class InviteComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly session = inject(AdminSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly Status = AdminInvitationStatus;

  private readonly token = this.route.snapshot.paramMap.get('token') ?? '';

  protected readonly preview = signal<AdminInvitationPreview | null>(null);
  protected readonly missing = signal(false);

  protected login = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.preview.set(await this.api.invitationPreview(this.token));
    } catch {
      this.missing.set(true);
    }
  }

  protected rolesTitle(roles: AdminRole[]): string {
    return roles.map(role => ROLE_TITLES[role] ?? `роль ${role}`).join(', ');
  }

  protected deadTitle(status: AdminInvitationStatus): string {
    return DEAD_TITLES[status] ?? 'Приглашение больше не действует.';
  }

  protected async accept(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      this.session.start(await this.api.acceptInvitation(this.token, this.login, this.password));
      await this.router.navigateByUrl('/');
    } catch (error) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 404
          ? 'неверный логин или пароль, слишком много попыток или ссылка уже не действует'
          : apiErrorText(error)
      );
    } finally {
      this.busy.set(false);
    }
  }
}
