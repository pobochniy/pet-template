import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AdminSessionService } from '../core/admin-session.service';
import { AdminApiService } from '../core/admin-api.service';
import { SECTIONS } from './sections';

/**
 * Оболочка: вкладки разделов по ролям из токена. Сессия кончается сроком токена
 * (спец. admin, 3.3) — до какого часа она живёт, видно в шапке — или кнопкой «Выйти»:
 * роли попадают в токен при входе, и новые видны только после перевхода
 */
@Component({
  selector: 'adm-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe],
  template: `
    <div class="container-fluid py-3">
      <div class="d-flex align-items-baseline gap-3 mb-3">
        <h1 class="h5 mb-0">PetTemplate · админка</h1>
        <span class="text-body-secondary small">
          {{ session.admin()?.userName }} · сессия до {{ session.expiresAt | date: 'HH:mm' }}
        </span>
        <button class="btn btn-sm btn-link ms-auto" type="button" id="logout" (click)="logout()">Выйти</button>
      </div>

      <ul class="nav nav-tabs mb-3">
        @for (section of sections(); track section.path) {
          <li class="nav-item">
            <a class="nav-link" [routerLink]="section.path" routerLinkActive="active">{{ section.title }}</a>
          </li>
        }
      </ul>

      <router-outlet />
    </div>
  `
})
export class ShellComponent {
  protected readonly session = inject(AdminSessionService);
  private readonly api = inject(AdminApiService);

  protected readonly sections = computed(() => SECTIONS.filter(section => this.session.hasRole(section.role)));

  /**
   * Сервер гасит куку шлюза (спец. admin, 3.3). Не ответил (токен уже истёк — тот же `404`) —
   * выходим всё равно: сам токен из хранилища стирает клиент
   */
  protected async logout(): Promise<void> {
    try {
      await this.api.logout();
    } catch {
      // выходим и без ответа
    }
    this.session.end();
  }
}
