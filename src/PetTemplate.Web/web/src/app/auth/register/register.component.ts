import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { createRegisterForm, setWorkspaceSkipped } from '../../shared/form-models/register-form.model';
import { EMAIL_MAX_LENGTH, USER_NAME_MAX_LENGTH } from '../../shared/form-models/account-rules';
import { AuthApiService } from '../../shared/api/auth-api.service';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { applyServerValidationErrors, serverValidationText } from '../../shared/core/server-validation.util';
import { ExternalButtonsComponent } from '../external-buttons/external-buttons.component';

@Component({
  selector: 'login-register',
  templateUrl: './register.component.html',
  styleUrls: ['./register.component.css'],
  imports: [ReactiveFormsModule, FormValidationComponent, ExternalButtonsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegisterComponent {
  protected readonly i18n = inject(I18nService);
  public readonly registerForm = createRegisterForm();

  protected readonly userNameMaxLength = USER_NAME_MAX_LENGTH;
  protected readonly emailMaxLength = EMAIL_MAX_LENGTH;

  /** «Меня пригласят — пропустить»: регистрация без пространства (спец. authorization, 3, п. 1) */
  readonly workspaceSkipped = signal(false);

  /**
   * Развёрнуты ли необязательные поля (телефон, почта). Свёрнуты по умолчанию, как в Lad:
   * обязательны только логин и пароли, а длинная форма на входе отпугивает сильнее,
   * чем радует возможность заполнить всё сразу
   */
  protected readonly showOptionalFields = signal(false);

  constructor(
    private service: AuthApiService,
    private router: Router,
    private alerts: AlertsService
  ) {}

  skipWorkspace(skipped: boolean) {
    this.workspaceSkipped.set(skipped);
    setWorkspaceSkipped(this.registerForm, skipped);
  }

  protected toggleOptionalFields(): void {
    this.showOptionalFields.update(shown => !shown);
  }

  async onSubmit() {
    this.registerForm.markAllAsTouched();
    if (!this.registerForm.valid) {
      // Ошибка в свёрнутом поле не должна прятаться: человек жмёт кнопку и не видит, почему
      const { phone, email } = this.registerForm.controls;
      if (phone.invalid || email.invalid) this.showOptionalFields.set(true);
      return;
    }

    try {
      const result = await this.service.register(this.registerForm, this.i18n.lang());

      // С пространством — на его страницу участников, как после создания из настроек:
      // заведя пространство, человек чаще всего идёт звать остальных
      const created = Object.keys(result.permissionsByWorkspace ?? {}).length > 0;
      await this.router.navigateByUrl(created ? '/workspace/members' : '/');
    } catch (e: unknown) {
      if ((e as { status?: number } | null)?.status === 422) {
        // 422 знает поля: раскладываем по ним. Отказ без поля — плашкой
        const unplaced = applyServerValidationErrors(this.registerForm, e);
        const { phone, email } = this.registerForm.controls;
        if (phone.invalid || email.invalid) this.showOptionalFields.set(true);
        for (const error of unplaced) {
          this.alerts.push('danger', serverValidationText(error, (code, params) => this.i18n.translateCode(code, params)), 10000);
        }
      } else {
        // Занятые логин, почта и телефон приезжают кодами `auth.error.*`
        this.alerts.push('danger', serverErrorMessage(e, this.i18n, this.i18n.t('auth.error.registerFailed')), 10000);
      }
    }
  }
}
