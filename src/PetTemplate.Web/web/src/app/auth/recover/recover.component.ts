import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { RecoveryApiService } from '../../shared/api/recovery-api.service';
import { createRecoverEmailControl } from '../form-models/recover-form.model';
import { environment } from '../../../environments/environment';
import { ExternalButtonsComponent } from '../external-buttons/external-buttons.component';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { AUTH_SCOPES } from '../auth.scopes';

/**
 * `/auth/recover` — «Забыли пароль?» (спец. accounts, 8.2): пути по порядку.
 *
 * 1. Войти через привязанный Telegram или VK (8.3) — только в существующий аккаунт
 *    (`existingOnly`): неизвестный вход новый аккаунт не заводит. Площадок на сервере
 *    нет — нет и этого пути. После входа профиль даёт задать новый пароль без старого.
 * 2. Письмо со ссылкой — по подтверждённой почте: письмо уходит на неё же, так что
 *    логин и телефон здесь не нужны. Ответ один на любую почту: форма не рассказывает,
 *    есть ли такой аккаунт.
 * 3. Поддержка — когда нет ни того, ни другого. Контакт — из настроек клиента
 *    (`environment.supportContact`): канал выбирает проект, созданный из шаблона
 */
@Component({
  selector: 'auth-recover',
  imports: [ReactiveFormsModule, RouterLink, ExternalButtonsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recover.component.html',
  styles: ['.auth-recover { max-width: 560px; }']
})
export class RecoverComponent {
  protected readonly i18n = injectI18n(...AUTH_SCOPES);

  protected readonly email = createRecoverEmailControl();
  protected readonly sent = signal(false);
  protected readonly busy = signal(false);
  protected readonly supportContact = environment.supportContact;
  /** Ссылку показываем человеческим адресом: `@ник` для Telegram, почта без `mailto:` */
  protected readonly supportLabel = this.supportContact
    .replace(/^https:\/\/t\.me\//, '@')
    .replace(/^mailto:/, '');

  constructor(private recovery: RecoveryApiService) {}

  protected async send(): Promise<void> {
    this.email.markAsTouched();
    if (this.email.invalid) return;

    this.busy.set(true);
    try {
      await this.recovery.requestReset(this.email.value.trim());
    } catch {
      // Тот же ответ и на отказ: отличать «нет аккаунта» от «сеть упала» форма не должна,
      // а повтор человек сделает сам, если письмо не придёт
    } finally {
      this.sent.set(true);
      this.busy.set(false);
    }
  }
}
