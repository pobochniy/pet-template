import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { ExternalProvider, ExternalPurpose } from '../../shared/models/external-login.model';
import { I18nService } from '../../shared/i18n/i18n.service';

/**
 * «Войти через Telegram» и «Войти через VK» под формой входа, регистрации, восстановления
 * и лендинга приглашения (спец. accounts, 5.1). Один сценарий для всех страниц: неизвестный
 * вход заводит аккаунт, известный — входит. Площадка, выключенная на сервере, кнопки не
 * получает; не включена ни одна — блока нет вовсе. Исключение — восстановление доступа
 * (`purpose="recover"`, 8.3): там только вход в существующий аккаунт
 */
@Component({
  selector: 'auth-external-buttons',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (enabled().telegram || enabled().vk) {
      <div class="d-grid gap-2 mt-3" id="external-logins">
        <div class="text-center form-text">{{ i18n.t('auth.external.or') }}</div>
        @if (enabled().telegram) {
          <button type="button" class="btn btn-outline-primary" id="login-telegram"
                  [disabled]="busy()" (click)="go('telegram')">
            {{ i18n.t('auth.external.telegram') }}
          </button>
        }
        @if (enabled().vk) {
          <button type="button" class="btn btn-outline-primary" id="login-vk"
                  [disabled]="busy()" (click)="go('vk')">
            {{ i18n.t('auth.external.vk') }}
          </button>
        }
        @if (failed()) {
          <div class="text-danger small text-center" id="external-start-failed">{{ i18n.t('auth.error.externalFailed') }}</div>
        }
      </div>
    }
  `
})
export class ExternalButtonsComponent implements OnInit {
  protected readonly i18n = inject(I18nService);

  /** Куда вернуться после входа: главная или приглашение, с которого начали */
  readonly returnUrl = input('/');

  /** Вход с заведением аккаунта (`login`) или только в существующий (`recover`) */
  readonly purpose = input<Extract<ExternalPurpose, 'login' | 'recover'>>('login');

  protected readonly enabled = signal<Record<ExternalProvider, boolean>>({ telegram: false, vk: false });
  protected readonly busy = signal(false);
  protected readonly failed = signal(false);

  constructor(private external: ExternalLoginService) {}

  async ngOnInit(): Promise<void> {
    this.enabled.set(await this.external.enabled());
  }

  protected async go(provider: ExternalProvider): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    try {
      await this.external.start(provider, this.purpose(), this.returnUrl());
    } catch {
      // Окно Telegram заблокировано или сервер не отдал параметры площадки
      this.failed.set(true);
    } finally {
      // VK уводит со страницы, Telegram — уводит или окно закрыли: кнопки снова живые
      this.busy.set(false);
    }
  }
}
