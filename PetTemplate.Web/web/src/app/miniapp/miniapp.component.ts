import { Component, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthApiService } from '../shared/api/auth-api.service';
import { AlertsService } from '../shared/alerts/alerts.service';

@Component({
  selector: 'app-miniapp',
  templateUrl: './miniapp.component.html',
  styleUrl: './miniapp.component.css',
  imports: []
})
export class MiniappComponent implements OnInit {
  isLoading = signal(true);
  authError = signal<{ code: number; message: string } | null>(null);

  constructor(
    private authService: AuthApiService,
    private router: Router,
    private alertsService: AlertsService
  ) {}

  private getInitData(): string | undefined {
    const telegramInitData = (window as any).Telegram?.WebApp?.initData as string | undefined;
    if (telegramInitData) return telegramInitData;

    const search = window.location.search;
    if (search) {
      const params = new URLSearchParams(search);
      if (params.has('hash')) return search.substring(1);
    }

    return undefined;
  }

  async ngOnInit() {
    try {
      const initData = this.getInitData();

      if (!initData) {
        this.alertsService.push('danger', 'Ошибка при подключении к Telegram');
        return;
      }

      await this.authService.telegramAuth(initData);

      if (this.authService.isAuth) {
        await this.router.navigateByUrl('/');
      } else {
        this.alertsService.push('danger', 'Ошибка аутентификации');
      }
    } catch (error) {
      console.error('Telegram authentication error:', error);
      this.alertsService.push('danger', 'Ошибка при подключении к Telegram');
      this.authError.set({
        code: 0,
        message: 'Ошибка при подключении к Telegram. Перезайдите в приложение через Telegram.'
      });
    } finally {
      this.isLoading.set(false);
    }
  }
}
