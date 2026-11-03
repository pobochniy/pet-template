import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { AuthApiService } from '../../shared/api/auth-api.service';
import { UserService } from '../../shared/core/user.service';
import { createLoginForm } from '../../shared/form-models/login-form.model';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { LanguageService } from '../../shared/i18n/language.service';
import { serverValidationErrors, serverValidationText } from '../../shared/core/server-validation.util';
import { ExternalButtonsComponent } from '../external-buttons/external-buttons.component';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { AUTH_SCOPES } from '../auth.scopes';

@Component({
  selector: 'login-auth',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css'],
  imports: [ReactiveFormsModule, RouterLink, FormValidationComponent, ExternalButtonsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LoginComponent {
  protected readonly i18n = injectI18n(...AUTH_SCOPES);
  private readonly language = inject(LanguageService);
  public readonly loginForm = createLoginForm();

  constructor(
    private service: AuthApiService,
    private userService: UserService,
    private router: Router,
    private alerts: AlertsService
  ) {}

  async onSubmit() {
    this.loginForm.markAllAsTouched();
    if (!this.loginForm.valid) return;

    try {
      const result = await this.service.login(this.loginForm);
      this.userService.applySession(result);
      // Язык профиля — выбор с другого устройства (спец. accounts, 8.4)
      await this.language.adopt(result?.profile?.language);
      await this.router.navigateByUrl('/');
    } catch (e: unknown) {
      // Лимит неудач (спец. accounts, 8.1) — своей фразой: «неверный пароль» здесь
      // увёл бы человека перебирать дальше, а верный пароль сейчас тоже не пустит
      if ((e as { error?: { code?: unknown } } | null)?.error?.code === 'auth.error.tooManyAttempts') {
        this.alerts.push('danger', this.i18n.t('auth.error.tooManyAttempts'), 10000);
        return;
      }

      // Неудачный вход — всегда своей фразой: подробности превратили бы форму
      // в подтверждатель чужих логинов. Иные отказы 422 (логин равен паролю) — их текстом
      const messages = this.validationMessages(e);
      if (messages.length > 0) this.alerts.push('danger', messages.join('\n'), 10000);
      else this.alerts.push('danger', this.i18n.t('auth.error.invalidCredentials'), 5000);
    }
  }

  /** Переведённые отказы 422, кроме «неверный логин или пароль» — тот показывается своей фразой */
  private validationMessages(error: unknown): string[] {
    return Object.values(serverValidationErrors(error))
      .flat()
      .filter(x => x.code !== 'auth.error.invalidCredentials')
      .map(x => serverValidationText(x, (code, params) => this.i18n.translateCode(code, params)));
  }
}
